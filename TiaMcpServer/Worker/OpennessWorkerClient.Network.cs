using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Worker;

public partial class OpennessWorkerClient
{
    /// <summary>
    /// Sends a <c>read_hardware_config</c> request to the worker. <paramref name="deviceName"/>
    /// narrows to exactly one device, <paramref name="plcName"/> selects the PLC used for tag
    /// matching, and <paramref name="includeIoDetails"/>/<paramref name="includeTagMatches"/> opt
    /// into the structured I/O map. All four default to no-narrowing/no-details so internal
    /// callers (notably <c>NetworkWritePlanner.ReadCurrentStateAsync</c>) stay lightweight.
    /// </summary>
    public Task<WorkerCallResult> ReadHardwareConfigAsync(
        string? deviceName = null,
        string? plcName = null,
        bool includeIoDetails = false,
        bool includeTagMatches = false)
    {
        return SendBoundProjectRequestAsync(
            "read_hardware_config",
            null,
            request =>
            {
                request.DeviceName = deviceName;
                request.PlcName = plcName;
                request.IncludeIoDetails = includeIoDetails;
                request.IncludeTagMatches = includeTagMatches;
            },
            "{}");
    }

    /// <summary>
    /// Sends the internal hardware-page candidate request under the same serialized host-binding
    /// lease as ordinary bound calls. Continuations additionally pin the host snapshot and the
    /// exact worker identity observed on the preceding response.
    /// </summary>
    public Task<HardwarePageWorkerCallResult> ReadHardwarePageCandidatesAsync(
        string? deviceName,
        string? plcName,
        bool includeIoDetails,
        bool includeTagMatches,
        int pageSize,
        HardwarePageContinuationInfo? continuation,
        ProjectBindingSnapshot? requiredHostBinding,
        WorkerSessionIdentity? expectedSessionIdentity)
        => ExecuteSerializedBindingOperationAsync(
            () => ReadHardwarePageCandidatesCoreAsync(
                deviceName,
                plcName,
                includeIoDetails,
                includeTagMatches,
                pageSize,
                continuation,
                requiredHostBinding,
                expectedSessionIdentity));

    private async Task<HardwarePageWorkerCallResult> ReadHardwarePageCandidatesCoreAsync(
        string? deviceName,
        string? plcName,
        bool includeIoDetails,
        bool includeTagMatches,
        int pageSize,
        HardwarePageContinuationInfo? continuation,
        ProjectBindingSnapshot? requiredHostBinding,
        WorkerSessionIdentity? expectedSessionIdentity)
    {
        var isContinuation = continuation is not null;
        if (isContinuation)
        {
            if (!TryValidateCompleteSessionIdentity(
                    expectedSessionIdentity,
                    out _))
            {
                return HardwarePageFailure(
                    _projectSessionBinding.CaptureSnapshot(),
                    WorkerFailureCategories.CursorBindingMismatch,
                    "The hardware-page continuation does not retain a complete worker session identity.");
            }
        }
        else
        {
            // Preserve ordinary read semantics for a configured startup project: verify it before
            // issuing the candidate read. An unbound host fails closed below.
            if (await GroundConfiguredProjectAsync().ConfigureAwait(false) is { } verification)
            {
                return new HardwarePageWorkerCallResult(
                    verification,
                    _projectSessionBinding.CaptureSnapshot());
            }
        }

        if (!_projectSessionBinding.TryResolveWithSnapshot(
                null,
                out var hostBinding,
                out var effectiveProjectPath,
                out var bindingError))
        {
            return HardwarePageFailure(
                hostBinding,
                isContinuation
                    ? WorkerFailureCategories.CursorBindingMismatch
                    : WorkerFailureCategories.BindingConflict,
                bindingError!);
        }

        if (!hostBinding.IsVerified)
        {
            return HardwarePageFailure(
                hostBinding,
                isContinuation
                    ? WorkerFailureCategories.CursorBindingMismatch
                    : WorkerFailureCategories.BindingConflict,
                ProjectSessionBinding.BindFirstMessage);
        }

        if (isContinuation &&
            (requiredHostBinding is null || !requiredHostBinding.SameBinding(hostBinding)))
        {
            return HardwarePageFailure(
                hostBinding,
                WorkerFailureCategories.CursorBindingMismatch,
                "The host project binding changed after the preceding hardware page.");
        }

        var request = new WorkerRequest
        {
            Method = "read_hardware_page_candidates",
            ProjectPath = effectiveProjectPath,
            ExpectedSessionIdentity = isContinuation
                ? expectedSessionIdentity
                : hostBinding.ToWorkerIdentity(),
            DeviceName = deviceName,
            PlcName = plcName,
            IncludeIoDetails = includeIoDetails,
            IncludeTagMatches = includeTagMatches,
            HardwarePageSize = pageSize,
            HardwarePageContinuation = continuation
        };

        var result = await InvokeWorkerAsync(request).ConfigureAwait(false);
        if (!result.Success)
        {
            return new HardwarePageWorkerCallResult(
                MapHardwarePageContinuationFailure(result, isContinuation),
                hostBinding);
        }

        if (!TryValidateCompleteSessionIdentity(result.SessionIdentity, out _))
        {
            return HardwarePageFailure(
                hostBinding,
                WorkerFailureCategories.ProtocolError,
                "The hardware-page worker response did not include a complete session identity.",
                result);
        }

        result = ValidateOrPromoteSessionIdentity(result, hostBinding);
        if (!result.Success)
        {
            return new HardwarePageWorkerCallResult(
                MapHardwarePageContinuationFailure(result, isContinuation),
                hostBinding);
        }

        if (isContinuation &&
            !SameSessionIdentity(expectedSessionIdentity!, result.SessionIdentity!))
        {
            return HardwarePageFailure(
                hostBinding,
                WorkerFailureCategories.CursorBindingMismatch,
                "The hardware-page continuation no longer matches the live worker session.",
                result);
        }

        return new HardwarePageWorkerCallResult(result, hostBinding);
    }

    private static WorkerCallResult MapHardwarePageContinuationFailure(
        WorkerCallResult result,
        bool isContinuation)
    {
        if (!isContinuation ||
            (!string.Equals(
                 result.FailureCategory,
                 WorkerFailureCategories.BindingConflict,
                 StringComparison.Ordinal) &&
             !string.Equals(
                 result.FailureCategory,
                 WorkerFailureCategories.CursorBindingMismatch,
                 StringComparison.Ordinal)))
        {
            return result;
        }

        return WorkerCallResult.Fail(
            WorkerFailureCategories.CursorBindingMismatch,
            result.Error ?? "The hardware-page continuation no longer matches the worker session.",
            result.Warnings) with
        {
            ResolvedProjectPath = result.ResolvedProjectPath,
            SessionIdentity = result.SessionIdentity
        };
    }

    private static HardwarePageWorkerCallResult HardwarePageFailure(
        ProjectBindingSnapshot hostBinding,
        string failureCategory,
        string error,
        WorkerCallResult? source = null)
        => new(
            WorkerCallResult.Fail(failureCategory, error, source?.Warnings) with
            {
                ResolvedProjectPath = source?.ResolvedProjectPath,
                SessionIdentity = source?.SessionIdentity
            },
            hostBinding);

    public Task<WorkerCallResult> SearchEquipmentCatalogAsync(string query, int? maxResults = null)
    {
        return SendBoundProjectRequestAsync(
            "search_equipment_catalog",
            null,
            request =>
            {
                request.Query = query;
                request.MaxResults = maxResults;
            },
            "[]");
    }

    /// <summary>
    /// Sends a <c>list_network_objects</c> request to the worker. <paramref name="objectKinds"/>
    /// is deep-copied into a new list so the worker-bound request never holds a reference to the
    /// caller's mutable collection.
    /// </summary>
    public Task<WorkerCallResult> ListNetworkObjectsAsync(
        IReadOnlyList<string> objectKinds,
        string? deviceName,
        int? pageSize,
        string? cursor)
    {
        return SendBoundProjectRequestAsync(
            "list_network_objects",
            null,
            request =>
            {
                request.NetworkObjectKinds = new List<string>(objectKinds);
                request.NetworkObjectDeviceName = deviceName;
                request.NetworkObjectPageSize = pageSize;
                request.NetworkObjectCursor = cursor;
            },
            "{}");
    }

    /// <summary>
    /// Sends an <c>inspect_network_object</c> request to the worker. The caller must supply a
    /// <see cref="NetworkObjectSelectorInfo"/> that was mapped from the host's
    /// <c>NetworkObjectTarget</c> (item-path segments deep-copied, caller list discarded).
    /// </summary>
    public Task<WorkerCallResult> InspectNetworkObjectAsync(
        NetworkObjectSelectorInfo target,
        IReadOnlyList<string>? attributeNames)
    {
        return SendBoundProjectRequestAsync(
            "inspect_network_object",
            null,
            request =>
            {
                request.NetworkObjectTarget = target;
                request.NetworkAttributeNames = attributeNames is null ? null : new List<string>(attributeNames);
            },
            "{}");
    }

    public Task<WorkerCallResult> AddNetworkDeviceAsync(
        string typeIdentifier,
        string deviceName,
        string deviceItemName)
    {
        return SendBoundProjectRequestAsync(
            "add_network_device",
            null,
            request =>
            {
                request.TypeIdentifier = typeIdentifier;
                request.DeviceName = deviceName;
                request.DeviceItemName = deviceItemName;
            },
            "{}");
    }

    public Task<WorkerCallResult> ConfigureNetworkDeviceAsync(NetworkObjectSelectorInfo target,
        string? ipAddress, string? subnetMask, string? pnDeviceName, bool? pnDeviceNameAutoGeneration, string? subnetId,
        string? ioSystemSubnetId, int? ioSystemNumber)
    {
        // Freeze before asynchronous binding/IPC work can yield to the caller.
        var prepared = JsonSerializer.Deserialize<NetworkObjectSelectorInfo>(JsonSerializer.Serialize(target, WorkerJson.Envelope), WorkerJson.Envelope)!;
        return SendBoundProjectRequestAsync(
            "configure_network_device",
            null,
            request =>
            {
                request.NetworkObjectTarget = prepared;
                request.DeviceName = prepared.DeviceName;
                request.NodeId = prepared.NodeId;
                request.IpAddress = ipAddress;
                request.SubnetMask = subnetMask;
                request.PnDeviceName = pnDeviceName;
                request.PnDeviceNameAutoGeneration = pnDeviceNameAutoGeneration;
                request.SubnetId = subnetId;
                request.IoSystemSubnetId = ioSystemSubnetId;
                request.IoSystemNumber = ioSystemNumber;
            },
            "{}");
    }

    /// <summary>
    /// Sends a <c>create_subnet</c> request. Never forwards <see cref="WorkerRequest.SubnetId"/> —
    /// a new subnet's id is assigned by Openness at creation time, not supplied by the caller.
    /// </summary>
    public Task<WorkerCallResult> CreateSubnetAsync(
        string name,
        string networkType,
        int? highestAddress,
        string? transmissionSpeed)
    {
        return SendBoundProjectRequestAsync(
            "create_subnet",
            null,
            request =>
            {
                request.SubnetName = name;
                request.SubnetNetworkType = networkType;
                request.SubnetHighestAddress = highestAddress;
                request.SubnetTransmissionSpeed = transmissionSpeed;
            },
            "{}");
    }

    /// <summary>
    /// Sends an <c>update_subnet</c> request. <paramref name="subnetId"/> is forwarded via the
    /// existing <see cref="WorkerRequest.SubnetId"/> field — there is no second identity field.
    /// Never forwards a network type: an existing subnet's type is not changeable through this
    /// contract.
    /// </summary>
    public Task<WorkerCallResult> UpdateSubnetAsync(
        string subnetId,
        string? name,
        int? highestAddress,
        string? transmissionSpeed)
    {
        return SendBoundProjectRequestAsync(
            "update_subnet",
            null,
            request =>
            {
                request.SubnetId = subnetId;
                request.SubnetName = name;
                request.SubnetHighestAddress = highestAddress;
                request.SubnetTransmissionSpeed = transmissionSpeed;
            },
            "{}");
    }

    /// <summary>
    /// Sends a <c>delete_subnet</c> request. Forwards only the target identity via the existing
    /// <see cref="WorkerRequest.SubnetId"/> field.
    /// </summary>
    public Task<WorkerCallResult> DeleteSubnetAsync(string subnetId)
    {
        return SendBoundProjectRequestAsync(
            "delete_subnet",
            null,
            request =>
            {
                request.SubnetId = subnetId;
            },
            "{}");
    }
}
