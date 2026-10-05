using System.Diagnostics;
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

/// <summary>
/// The only decoder of Network worker success payloads. Every network operation declares exactly
/// one result contract here; a payload that does not match it is rejected rather than forwarded.
///
/// <para>
/// Rejection is deliberate. Forwarding an unrecognized payload would publish worker-shaped data
/// under a declared output schema that does not describe it, so malformed, unknown, incorrectly
/// cased, incorrectly typed, and structurally invalid payloads all become failed items with
/// category <see cref="WorkerFailureCategories.ProtocolError"/>. The rejected payload is never
/// echoed back — the caller asked for contract-shaped data and must not be handed the raw bytes
/// that failed the contract.
/// </para>
/// </summary>
public static class NetworkPayloadContract
{
    /// <summary>Projects one worker outcome into its structured batch item.</summary>
    public static StructuredOperationItem Project(
        NetworkOperationRequest operation,
        WorkerCallResult workerResult)
        => Project(operation, workerResult, false);

    public static StructuredOperationItem Project(
        NetworkOperationRequest operation,
        WorkerCallResult workerResult,
        bool requireVerification)
        => Project(operation, workerResult, Console.Error.WriteLine, requireVerification);

    internal static StructuredOperationItem Project(
        NetworkOperationRequest operation,
        WorkerCallResult workerResult,
        Action<string> writeProtocolDiagnostic,
        bool requireVerification = false)
    {
        ArgumentNullException.ThrowIfNull(writeProtocolDiagnostic);
        var warnings = workerResult.Warnings ?? Array.Empty<string>();

        if (!workerResult.Success)
        {
            return Failed(
                operation,
                workerResult.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                workerResult.Error ?? $"Network operation '{operation.Operation}' failed.",
                warnings);
        }

        JsonElement result;
        try
        {
            result = Decode(operation, workerResult.Payload, requireVerification);
        }
        catch (JsonException exception)
        {
            TryWriteProtocolDiagnostic(writeProtocolDiagnostic, operation.Operation, exception);
            return Failed(
                operation,
                WorkerFailureCategories.ProtocolError,
                $"The worker payload for '{operation.Operation}' did not match its declared result "
                    + "contract and was rejected.",
                warnings);
        }

        // A decoded configuration payload describes the completed attempt, including settings
        // the worker could not apply. Retain that evidence while failing any requested skip.
        var settingsSkipped = operation.Operation == "configure_network_device"
            && result.GetProperty("skippedSettings").EnumerateObject().Any();
        var postconditionFailed = result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty("verification", out var evidence)
            && evidence.ValueKind == JsonValueKind.Object
            && evidence.GetProperty("status").GetString() is "failed" or "unverified";

        return new StructuredOperationItem(
            operation.OperationId,
            operation.Operation,
            settingsSkipped || postconditionFailed ? OperationBatchStatus.Failed : OperationBatchStatus.Succeeded,
            result,
            Failure: settingsSkipped
                ? new StructuredOperationFailure(
                    WorkerFailureCategories.WorkerOperationFailed,
                    "One or more requested network settings could not be applied.")
                : postconditionFailed
                    ? new StructuredOperationFailure(WorkerFailureCategories.PostconditionFailed,
                        "Immediate network postconditions failed or could not be verified. Inspect current state before retrying.")
                    : null,
            Omission: null,
            SkipReason: null,
            warnings);
    }

    /// <summary>Compacts only declared diagnostic members, preserving exact recovery values.</summary>
    internal static JsonElement CompactWriteDiagnostics(JsonElement result, Func<string, string> message,
        Func<IReadOnlyList<string>, IReadOnlyList<string>> warnings)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(result.GetRawText())!.AsObject();
        void Message(System.Text.Json.Nodes.JsonObject value)
        {
            if (value["message"] is { } text) value["message"] = message(text.GetValue<string>());
        }
        foreach (var member in new[] { "messages", "warnings" })
            if (root[member] is System.Text.Json.Nodes.JsonArray values)
                root[member] = new System.Text.Json.Nodes.JsonArray(warnings(values.Select(v => v!.GetValue<string>()).ToArray())
                    .Select(v => (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(v)).ToArray());
        if (root["verification"] is System.Text.Json.Nodes.JsonObject evidence)
        {
            Message(evidence);
            if (evidence["checks"] is System.Text.Json.Nodes.JsonArray checks)
                foreach (var check in checks) Message(check!.AsObject());
        }
        return CanonicalJson.ToElement(root);
    }
    private static void TryWriteProtocolDiagnostic(
        Action<string> writeProtocolDiagnostic,
        string operation,
        JsonException exception)
    {
        try
        {
            var validators = new StackTrace(exception, fNeedFileInfo: false)
                .GetFrames()
                .Select(frame => frame.GetMethod())
                .Where(method => method?.DeclaringType == typeof(NetworkPayloadContract))
                .Select(method => method!.Name)
                .Where(name => !string.Equals(name, nameof(Project), StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Take(6)
                .ToArray();
            var validatorChain = validators.Length == 0
                ? "unavailable"
                : string.Join(">", validators);
            var diagnostic = "TiaMcpServer: worker payload contract rejection: "
                + $"operation={operation}; validators={validatorChain}.";
            writeProtocolDiagnostic(diagnostic.Length <= 512 ? diagnostic : diagnostic[..512]);
        }
        catch
        {
            // Diagnostics must never replace the stable fail-closed protocol_error response.
        }
    }

    /// <summary>
    /// Decodes a <c>read_hardware_config</c> payload under the same contract the batch path uses,
    /// returning the typed value. Callers that bind safety tokens to hardware state need the value
    /// itself, not a batch item. Throws <see cref="JsonException"/> when the payload does not match.
    /// </summary>
    public static HardwareConfigInfo DecodeHardwareConfig(string payload)
        => CanonicalJson.NormalizeWorkerPayload<HardwareConfigInfo>(
            payload,
            cfg => ValidateHardwareConfig(cfg, includeIoDetails: null)).Value;

    private static JsonElement Decode(NetworkOperationRequest operation, string payload, bool requireVerification) => operation.Operation switch
    {
        "read_hardware_config" => Decode<HardwareConfigInfo>(
            payload,
            cfg => ValidateHardwareConfig(cfg, operation.IncludeIoDetails ?? false)),
        "search_equipment_catalog" => Decode<CatalogEntryInfo[]>(payload, ValidateCatalogEntries),
        "add_network_device" => Decode<AddDeviceResultInfo>(payload, value => ValidateAddedDevice(operation, value, requireVerification)),
        "configure_network_device" => Decode<ConfigureNetworkDeviceResultInfo>(payload, value => ValidateConfiguration(operation, value, requireVerification)),
        "list_network_objects" => Decode<NetworkObjectListInfo>(payload, ValidateObjectList),
        "inspect_network_object" => Decode<NetworkObjectInspectionInfo>(payload, ValidateObjectInspection),
        "create_subnet" or "update_subnet" or "delete_subnet" =>
            Decode<SubnetLifecycleResultInfo>(payload, value => ValidateSubnetMutation(operation, value, requireVerification)),
        _ => throw new JsonException($"No declared result contract for network operation '{operation.Operation}'."),
    };

    private static JsonElement Decode<T>(string payload, Action<T>? validate = null)
        => CanonicalJson.NormalizeWorkerPayload(payload, validate).Element;

    private static string Number(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static void ValidateAddedDevice(NetworkOperationRequest op, AddDeviceResultInfo value, bool required)
    {
        if (value.Verification is null && !required) return;
        var itemName = op.DeviceItemName ?? op.DeviceName;
        if (value.DeviceName != op.DeviceName || value.RootItemName != itemName || value.TypeIdentifier != op.TypeIdentifier)
            throw new JsonException("Added device identity contradicts the request.");
        ValidateVerification(value.Verification, new() { DeviceName = value.DeviceName, DeviceItemName = value.RootItemName },
            new() { ["deviceName"] = op.DeviceName, ["deviceItemName"] = itemName, ["typeIdentifier"] = op.TypeIdentifier });
    }

    private static void ValidateConfiguration(NetworkOperationRequest op, ConfigureNetworkDeviceResultInfo value, bool required)
    {
        if (value.Verification is null && !required) return;
        if (value.DeviceName != op.Target?.DeviceName || string.IsNullOrWhiteSpace(op.Target?.NodeId))
            throw new JsonException("Configuration identity contradicts the request.");
        var requested = new Dictionary<string, string>(StringComparer.Ordinal);
        var changes = op.Changes;
        if (changes?.IpAddress is { } address) requested.Add("Address", address);
        if (changes?.SubnetMask is { } mask) requested.Add("SubnetMask", mask);
        if (changes?.PnDeviceNameAutoGeneration is { } generated) requested.Add("PnDeviceNameAutoGeneration", generated ? "true" : "false");
        if (changes?.PnDeviceName is { } pn) requested.Add("PnDeviceName", pn);
        if (changes?.Subnet is { } subnet) requested.Add("Subnet", subnet.SubnetId!);
        if (changes?.IoSystem is { Number: { } number }) requested.Add("IoSystem", Number(number));
        var accounted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var setting in value.AppliedSettings)
        {
            if (!requested.TryGetValue(setting.Key, out var expected) || expected != setting.Value || !accounted.Add(setting.Key))
                throw new JsonException("Applied settings contradict the request.");
        }
        foreach (var setting in value.SkippedSettings)
        {
            if (!requested.ContainsKey(setting.Key) || string.IsNullOrWhiteSpace(setting.Value) || !accounted.Add(setting.Key))
                throw new JsonException("Skipped settings contradict the request.");
        }
        if (!accounted.SetEquals(requested.Keys)) throw new JsonException("Requested settings are unaccounted for.");
        var checks = value.AppliedSettings.ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.Ordinal);
        // One applied IoSystem setting is verified as two scalar checks: its subnet and number.
        if (checks.Remove("IoSystem"))
        {
            if (string.IsNullOrWhiteSpace(changes?.IoSystem?.SubnetId)) throw new JsonException("IO system subnet identity is required.");
            checks["IoSystemSubnet"] = changes!.IoSystem!.SubnetId;
            checks["IoSystemNumber"] = Number(changes.IoSystem.Number!.Value);
        }
        // A missing request position becomes -1, which the path validator rejects.
        var identity = new NetworkMutationIdentityInfo
        {
            DeviceName = value.DeviceName, NodeId = op.Target!.NodeId!, InterfaceName = op.Target.InterfaceName,
            InterfacePath = op.Target.InterfacePath?.Select(x => new NetworkInterfacePathSegmentInfo
                { Name = x.Name, PositionNumber = x.PositionNumber ?? -1, TypeIdentifier = x.TypeIdentifier }).ToList()
                ?? op.Target.ItemPath?.Select(x => new NetworkInterfacePathSegmentInfo
                { Name = x.Name, PositionNumber = x.PositionNumber ?? -1, TypeIdentifier = x.TypeIdentifier }).ToList(),
        };
        ValidateVerification(value.Verification,
            identity, checks,
            allowNotRequired: checks.Count == 0);
    }

    private static void ValidateSubnetMutation(NetworkOperationRequest op, SubnetLifecycleResultInfo value, bool required)
    {
        ValidateSubnetLifecycleResult(value);
        if (value.Verification is null && !required) return;
        if (op.Operation != "create_subnet" && value.SubnetId != op.Target?.SubnetId)
            throw new JsonException("Subnet identity contradicts the request.");
        var checks = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (op.Operation == "delete_subnet")
        {
            checks.Add("subnetAbsent", "true");
            checks.Add("affectedNodesPreserved", "true");
            checks.Add("affectedConnectionsRemoved", "true");
        }
        else
        {
            checks.Add("subnetIdentity", value.SubnetId);
            var name = op.Operation == "create_subnet" ? op.Subnet?.Name : op.SubnetChanges?.Name;
            if (name is not null) checks.Add("Name", name);
            if (op.Operation == "create_subnet") checks.Add("TypeIdentifier", "System:Subnet." + op.Subnet?.NetworkType);
            var address = op.Operation == "create_subnet" ? op.Subnet?.HighestAddress : op.SubnetChanges?.HighestAddress;
            if (address is { } number) checks.Add("HighestAddress", Number(number));
            var speed = op.Operation == "create_subnet" ? op.Subnet?.TransmissionSpeed : op.SubnetChanges?.TransmissionSpeed;
            if (speed is not null) checks.Add("TransmissionSpeed", speed);
        }
        var countChecks = value.Verification?.Checks.Where(check => check?.Name == "networkDeviceCountUnchanged").ToList();
        if (countChecks?.Count != 1) throw new JsonException("Exactly one root count check is required.");
        var count = countChecks[0];
        // The pre-mutation root count belongs to the worker observation. Validate its domain,
        // post-read value and summary, without pretending the host observed the before count.
        if (count is null || !int.TryParse(count.Expected, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var before) || before < 0)
            throw new JsonException("Root device count evidence is required.");
        if (count.Status != "unverified" && count.Observed != Number(value.NetworkDeviceCount)
            || value.NetworkDeviceCountUnchanged != (count.Status == "passed"))
            throw new JsonException("Root device count evidence contradicts the result.");
        checks.Add("networkDeviceCountUnchanged", count.Expected);
        ValidateVerification(value.Verification, new() { SubnetId = value.SubnetId }, checks);
    }

    private static void ValidateVerification(NetworkMutationVerificationInfo? evidence,
        NetworkMutationIdentityInfo identity, Dictionary<string, string?> expected, bool allowNotRequired = false)
    {
        if (evidence is null) throw new JsonException("Immediate mutation verification is required.");
        if (!SameIdentity(identity, evidence.Identity))
            throw new JsonException("Verification identity contradicts the result or request.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var check in evidence.Checks)
        {
            if (check is null || !seen.Add(check.Name) || !expected.TryGetValue(check.Name, out var target) || check.Expected != target)
                throw new JsonException("Verification contains missing, duplicate, unexpected or contradictory checks.");
            if (check.Status is not ("passed" or "failed" or "unverified")) throw new JsonException("Invalid check status.");
            if (check.Status == "passed" && check.Expected != check.Observed
                || check.Status == "failed" && check.Expected == check.Observed
                || check.Status == "unverified" && check.Observed is not null
                || check.Status != "passed" && string.IsNullOrWhiteSpace(check.Message))
                throw new JsonException("Verification status contradicts its evidence.");
        }
        if (!seen.SetEquals(expected.Keys)) throw new JsonException("Required checks are missing.");
        var status = evidence.Checks.Any(check => check.Status == "failed") ? "failed"
            : evidence.Checks.Any(check => check.Status == "unverified") ? "unverified"
            : evidence.Checks.Count == 0 && allowNotRequired ? "not_required" : "passed";
        if (evidence.Status != status || evidence.Checks.Count == 0 && !allowNotRequired)
            throw new JsonException("Verification summary contradicts its checks.");
    }

    private static bool SameIdentity(NetworkMutationIdentityInfo expected, NetworkMutationIdentityInfo actual)
    {
        var scalars = new[]
        {
            (expected.DeviceName, actual.DeviceName), (expected.DeviceItemName, actual.DeviceItemName),
            (expected.NodeId, actual.NodeId), (expected.InterfaceName, actual.InterfaceName), (expected.SubnetId, actual.SubnetId),
        };
        if (scalars.Any(pair => pair.Item1 is not null && string.IsNullOrWhiteSpace(pair.Item1) || pair.Item1 != pair.Item2)) return false;
        if (expected.InterfacePath is null || actual.InterfacePath is null) return expected.InterfacePath is null && actual.InterfacePath is null;
        // The canonical encoder validates both paths (nonempty, named, nonnegative positions) and compares them exactly.
        return NetworkInterfacePathEncoding.Encode(expected.InterfacePath) == NetworkInterfacePathEncoding.Encode(actual.InterfacePath);
    }

    // The worker-payload reader rejects a missing member and an explicit null in any member the
    // contract types declare non-nullable. It cannot see a null element inside a collection, or a
    // rule that depends on the request or on another member, so these validators check those.

    private static void ValidateHardwareConfig(HardwareConfigInfo value, bool? includeIoDetails)
    {
        if (value.RootDeviceCount < 0) throw new JsonException("'rootDeviceCount' must not be negative.");
        if (value.DiscoveryEvidence is { } discovery) ValidateDiscoveryEvidence(discovery);
        foreach (var device in value.Devices)
        {
            RequireNotNull(device, "devices[]");
            foreach (var item in device.Items)
            {
                ValidateDeviceItem(item, "devices[].items[]", includeIoDetails);
            }
        }

        foreach (var subnet in value.Subnets)
        {
            RequireNotNull(subnet, "subnets[]");
            if (subnet.ConnectionEvidence is { } connections)
            {
                ValidateConnectionMessages(connections.Complete, connections.Messages);
                foreach (var identity in connections.Nodes)
                {
                    RequireNotNull(identity, "connectionEvidence.nodes[]");
                    if (string.IsNullOrWhiteSpace(identity.DeviceName) || string.IsNullOrWhiteSpace(identity.NodeId))
                        throw new JsonException("Connected nodes require exact deviceName and nodeId identities.");
                    if (identity.InterfacePath is { } ownerPath)
                        ValidateInterfacePath(ownerPath, "connectionEvidence.nodes[].interfacePath");
                    if (identity.InterfaceName is not null)
                    {
                        if (identity.InterfacePath is null || string.IsNullOrWhiteSpace(identity.InterfaceName))
                            throw new JsonException("Connected node interfaceName requires an owner path and must be nonblank.");
                    }
                }
            }
            ValidateHardwareSelector(
                subnet.Selectable,
                subnet.Selector,
                subnet.SelectorDiagnostics,
                "subnets[]",
                NetworkObjectKinds.Subnet);
            foreach (var ioSystem in subnet.IoSystems)
            {
                RequireNotNull(ioSystem, "subnets[].ioSystems[]");
                ValidateHardwareSelector(
                    ioSystem.Selectable,
                    ioSystem.Selector,
                    ioSystem.SelectorDiagnostics,
                    "subnets[].ioSystems[]",
                    NetworkObjectKinds.IoSystem);
            }
        }
    }

    /// <summary>
    /// Recurses into a device item's nested network interfaces, nodes, and child items — the exact
    /// tree <see cref="NetworkIdentityResolver"/> walks to match a configure_network_device target.
    /// A null element anywhere in that walk must fail the contract here rather than reach the
    /// resolver, which would otherwise dereference it.
    /// </summary>
    private static void ValidateDeviceItem(DeviceItemInfo? item, string path, bool? includeIoDetails)
    {
        RequireNotNull(item, path);
        ValidateHardwareSelector(
            item!.Selectable,
            item.Selector,
            item.SelectorDiagnostics,
            path,
            NetworkObjectKinds.DeviceItem);

        if (includeIoDetails == true)
        {
            if (item.IoDetails is null)
            {
                throw new JsonException($"'{path}.ioDetails' is required when includeIoDetails is true.");
            }
        }
        else if (includeIoDetails == false)
        {
            if (item.IoDetails is not null)
            {
                throw new JsonException($"'{path}.ioDetails' is unexpected when includeIoDetails is false or omitted.");
            }
        }

        if (item.IoDetails is { } ioDetails)
        {
            ValidateIoDetails(ioDetails, $"{path}.ioDetails");
        }

        foreach (var connection in item.CommunicationConnections)
        {
            RequireNotNull(connection, $"{path}.communicationConnections[]");
            ValidateHardwareSelector(
                connection.Selectable,
                connection.Selector,
                connection.SelectorDiagnostics,
                $"{path}.communicationConnections[]",
                NetworkObjectKinds.CommunicationConnection);
        }

        foreach (var networkInterface in item.NetworkInterfaces)
        {
            RequireNotNull(networkInterface, $"{path}.networkInterfaces[]");
            ValidateHardwareSelector(
                networkInterface!.Selectable,
                networkInterface.Selector,
                networkInterface.SelectorDiagnostics,
                $"{path}.networkInterfaces[]",
                NetworkObjectKinds.NetworkInterface);
            foreach (var node in networkInterface.Nodes)
            {
                RequireNotNull(node, $"{path}.networkInterfaces[].nodes[]");
                if (node!.ConnectionEvidence is { } connection)
                {
                    ValidateConnectionMessages(connection.Complete, connection.Messages);
                    if (connection.SubnetId is not null && string.IsNullOrWhiteSpace(connection.SubnetId)
                        || connection.IoSystemSubnetId is not null && string.IsNullOrWhiteSpace(connection.IoSystemSubnetId)
                        || connection.IoSystemNumber < 0
                        || (connection.IoSystemSubnetId is null) != (connection.IoSystemNumber is null))
                        throw new JsonException("Node connection identities must be complete or explicitly null.");
                }
                ValidateHardwareSelector(
                    node!.Selectable,
                    node.Selector,
                    node.SelectorDiagnostics,
                    $"{path}.networkInterfaces[].nodes[]",
                    NetworkObjectKinds.Node);
            }
        }

        foreach (var child in item.Items)
        {
            ValidateDeviceItem(child, $"{path}.items[]", includeIoDetails);
        }
    }

    private static void ValidateConnectionMessages(bool complete, List<string> messages)
    {
        if (messages.Any(string.IsNullOrWhiteSpace) || !complete && messages.Count == 0)
            throw new JsonException("Incomplete connection evidence requires nonblank diagnostics.");
    }

    private static void ValidateDiscoveryEvidence(HardwareDiscoveryEvidenceInfo evidence)
    {
        if (evidence.Scope is not ("project" or "device"))
            throw new JsonException("Discovery scope must be project or device.");
        if (evidence.Complete != (evidence.Failures.Count == 0))
            throw new JsonException("Discovery completeness contradicts its failures.");
        foreach (var failure in evidence.Failures)
        {
            RequireNotNull(failure, "discoveryEvidence.failures[]");
            if (failure.Stage is not ("deviceEnumeration" or "deviceMaterialization"
                or "deviceItemEnumeration" or "deviceItemMaterialization" or "interfaceDiscovery"
                or "nodeEnumeration" or "nodeMaterialization" or "subnetEnumeration" or "subnetMaterialization"
                or "ioSystemEnumeration" or "ioSystemMaterialization" or "deviceSelection"))
                throw new JsonException("Unknown discovery failure stage.");
            if (failure.Stage == "deviceSelection" && evidence.Scope != "device")
                throw new JsonException("Device selection failure must be device-scoped.");
            if (string.IsNullOrWhiteSpace(failure.Message))
                throw new JsonException("Discovery failure message must be nonblank.");
        }
    }

    private static void ValidateInterfacePath(List<NetworkInterfacePathSegmentInfo> path, string prefix)
    {
        if (path.Count == 0) throw new JsonException($"'{prefix}' must be non-empty.");
        foreach (var segment in path)
        {
            RequireNotNull(segment, $"{prefix}[]");
            if (string.IsNullOrWhiteSpace(segment.Name) || segment.PositionNumber < 0
                || segment.TypeIdentifier is not null && string.IsNullOrWhiteSpace(segment.TypeIdentifier))
                throw new JsonException($"'{prefix}[]' requires nonblank name, nonnegative positionNumber and nonblank optional typeIdentifier.");
        }
    }

    /// <summary>
    /// The I/O map is present only when a read requested <c>includeIoDetails</c>. The reader keeps
    /// its declared collections non-null; this keeps a null element out of every one of them, so a
    /// consumer walking the tree never meets one.
    /// </summary>
    private static void ValidateIoDetails(DeviceItemIoDetailsInfo ioDetails, string path)
    {
        foreach (var address in ioDetails.Addresses)
        {
            RequireNotNull(address, $"{path}.addresses[]");
            foreach (var controllerName in address!.ControllerNames)
            {
                RequireNotNull(controllerName, $"{path}.addresses[].controllerNames[]");
            }

            if (address.StartAddress < 0)
            {
                throw new JsonException($"'{path}.addresses[].startAddress' must not be negative.");
            }

            if (address.Length < 0)
            {
                throw new JsonException($"'{path}.addresses[].length' must not be negative.");
            }
        }

        foreach (var channel in ioDetails.Channels)
        {
            RequireNotNull(channel, $"{path}.channels[]");
            if (channel!.Number < 0)
            {
                throw new JsonException($"'{path}.channels[].number' must not be negative.");
            }

            if (channel.ChannelAddressBits < 0)
            {
                throw new JsonException($"'{path}.channels[].channelAddressBits' must not be negative.");
            }

            foreach (var tagMatch in channel.TagMatches)
            {
                RequireNotNull(tagMatch, $"{path}.channels[].tagMatches[]");
            }
        }
    }

    private static void ValidateHardwareSelector(
        bool selectable,
        NetworkObjectSelectorInfo? selector,
        List<string> diagnostics,
        string path,
        string expectedKind)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (string.IsNullOrWhiteSpace(diagnostic))
            {
                throw new JsonException(
                    $"'{path}.selectorDiagnostics[]' must contain only non-blank strings.");
            }
        }

        if (selectable != (selector is not null))
        {
            throw new JsonException(
                $"'{path}.selectable' must agree exactly with '{path}.selector' presence.");
        }

        if (selectable)
        {
            if (diagnostics.Count != 0)
            {
                throw new JsonException(
                    $"'{path}.selectorDiagnostics' must be empty when the object is selectable.");
            }

            ValidateSelector(selector!, $"{path}.selector", expectedKind);
            return;
        }

        if (diagnostics.Count == 0)
        {
            throw new JsonException(
                $"'{path}.selectorDiagnostics' must explain why the object is unselectable.");
        }
    }

    private static void ValidateCatalogEntries(CatalogEntryInfo[] value)
    {
        foreach (var entry in value)
        {
            RequireNotNull(entry, "[]");
        }
    }

    private static void ValidateSubnetLifecycleResult(SubnetLifecycleResultInfo value)
    {
        if (string.IsNullOrWhiteSpace(value.SubnetId))
        {
            throw new JsonException("'subnetId' must be a nonblank string.");
        }

        if (string.IsNullOrWhiteSpace(value.Name))
        {
            throw new JsonException("'name' must be a nonblank string.");
        }

        if (value.NetworkDeviceCount < 0)
        {
            throw new JsonException("'networkDeviceCount' must not be negative.");
        }

        if (!value.NetworkDeviceCountUnchanged && value.Verification is null)
        {
            throw new JsonException("'networkDeviceCountUnchanged' must be true.");
        }
    }

    private static void ValidateObjectList(NetworkObjectListInfo value)
    {
        if (value.TotalCount < 0)
        {
            throw new JsonException("'totalCount' must not be negative.");
        }

        if (value.ReturnedCount < 0)
        {
            throw new JsonException("'returnedCount' must not be negative.");
        }

        if (value.Items.Count != value.ReturnedCount)
        {
            throw new JsonException(
                $"'returnedCount' ({value.ReturnedCount}) does not match 'items' count ({value.Items.Count}).");
        }

        if (value.ReturnedCount > value.TotalCount)
        {
            throw new JsonException(
                $"'returnedCount' ({value.ReturnedCount}) exceeds 'totalCount' ({value.TotalCount}).");
        }

        foreach (var item in value.Items)
        {
            RequireNotNull(item, "items[]");
            ValidateObjectSummary(item!);
        }
    }

    private static void ValidateObjectSummary(NetworkObjectSummaryInfo item)
    {
        if (string.IsNullOrWhiteSpace(item.Kind) || !NetworkObjectKinds.All.Contains(item.Kind))
        {
            throw new JsonException(
                $"'items[].kind' value '{item.Kind ?? "null"}' is not a recognised network object kind.");
        }

        foreach (var segment in item.Evidence.DeviceItemPath)
        {
            RequireNotNull(segment, "items[].evidence.deviceItemPath[]");
        }

        foreach (var diagnostic in item.Diagnostics)
        {
            RequireNotNull(diagnostic, "items[].diagnostics[]");
        }

        if (item.Selectable != (item.Selector is not null))
        {
            throw new JsonException(
                "'items[].selectable' must agree with whether 'items[].selector' is present.");
        }

        if (!item.Selectable
            && !item.Diagnostics.Any(diagnostic => !string.IsNullOrWhiteSpace(diagnostic)))
        {
            throw new JsonException(
                "An unselectable item requires at least one nonblank selector diagnostic.");
        }

        if (item.Selector is not null)
        {
            ValidateSelector(item.Selector, "items[].selector", item.Kind);
        }
    }

    private static readonly IReadOnlySet<string> ValidAttributeAccess =
        new HashSet<string>(StringComparer.Ordinal)
            { "none", "readOnly", "writeOnly", "readWrite", "unknown" };

    private static readonly IReadOnlySet<string> ValidAttributeAvailability =
        new HashSet<string>(StringComparer.Ordinal)
            { "available", "notApplicable", "unsupported", "unreadable", "readFailed", "unrepresentable", "unknownAttribute" };

    private static readonly IReadOnlySet<string> ValidAttributeSource =
        new HashSet<string>(StringComparer.Ordinal)
            { "modeled", "dynamic", "modeledAndDynamic" };

    private static readonly IReadOnlySet<string> SupportedConnectionTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "S7Connection",
            "FdlConnection",
            "IsoConnection",
            "IsoOnTcpConnection",
            "PtpConnection",
            "TcpConnection",
            "UdpConnection",
            "HmiConnection",
        };

    private static readonly IReadOnlySet<string> ValidAttributeValueKind =
        new HashSet<string>(StringComparer.Ordinal)
            { "null", "string", "boolean", "integer", "number", "enum" };

    private static void ValidateObjectInspection(NetworkObjectInspectionInfo value)
    {
        foreach (var segment in value.Evidence.DeviceItemPath)
        {
            RequireNotNull(segment, "evidence.deviceItemPath[]");
        }

        ValidateSelector(value.Target, "target");

        // Duplicate attribute names are a protocol error: the worker must return each name at most once.
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attr in value.Attributes)
        {
            RequireNotNull(attr, "attributes[]");
            ValidateAttribute(attr!);
            if (!seenNames.Add(attr.Name))
            {
                throw new JsonException($"Duplicate attribute name '{attr.Name}' in 'attributes'.");
            }
        }
    }

    private static void ValidateAttribute(NetworkAttributeInfo attr)
    {
        var prefix = $"attributes['{attr.Name}']";
        foreach (var supportedType in attr.SupportedTypes)
        {
            RequireNotNull(supportedType, $"{prefix}.supportedTypes[]");
        }

        if (!ValidAttributeAccess.Contains(attr.Access))
        {
            throw new JsonException(
                $"'{prefix}.access' value '{attr.Access}' is not valid. "
                + $"Valid values: {string.Join(", ", ValidAttributeAccess)}.");
        }

        if (!ValidAttributeAvailability.Contains(attr.Availability))
        {
            throw new JsonException(
                $"'{prefix}.availability' value '{attr.Availability}' is not valid. "
                + $"Valid values: {string.Join(", ", ValidAttributeAvailability)}.");
        }

        var isUnknownAttribute = string.Equals(attr.Availability, "unknownAttribute", StringComparison.Ordinal);
        if (isUnknownAttribute)
        {
            if (attr.Source is not null)
            {
                throw new JsonException(
                    $"'{prefix}.source' must be null when availability is 'unknownAttribute' (received '{attr.Source}').");
            }

            if (!string.Equals(attr.Access, "unknown", StringComparison.Ordinal)
                || attr.SupportedTypes.Count != 0
                || attr.Value is not null
                || attr.Diagnostic is null
                || !string.Equals(attr.Diagnostic.Category, "unknown_attribute", StringComparison.Ordinal))
            {
                throw new JsonException(
                    $"'{prefix}' must use access 'unknown', empty supportedTypes, no value, and an "
                    + "'unknown_attribute' diagnostic when availability is 'unknownAttribute'.");
            }
        }
        else
        {
            if (attr.Source is null || !ValidAttributeSource.Contains(attr.Source))
            {
                throw new JsonException(
                    $"'{prefix}.source' value '{attr.Source ?? "null"}' is not valid when availability is '{attr.Availability}'. "
                    + $"Valid values: {string.Join(", ", ValidAttributeSource)}.");
            }
        }

        if (string.Equals(attr.Availability, "available", StringComparison.Ordinal)
            && attr.Value is null)
        {
            throw new JsonException(
                $"'{prefix}.value' must use a typed value object when availability is 'available'.");
        }

        if (attr.Value is { Kind: var kind } value)
        {
            if (!ValidAttributeValueKind.Contains(kind))
            {
                throw new JsonException(
                    $"'{prefix}.value.kind' value '{kind}' is not valid. "
                    + $"Valid values: {string.Join(", ", ValidAttributeValueKind)}.");
            }

            if (!string.Equals(attr.Availability, "available", StringComparison.Ordinal))
            {
                throw new JsonException(
                    $"'{prefix}.value' must be null when availability is '{attr.Availability}'.");
            }

            ValidateAttributeValue(value, prefix);
        }
    }

    private static void ValidateSelector(
        NetworkObjectSelectorInfo target,
        string prefix,
        string? expectedKind = null)
    {
        if (string.IsNullOrWhiteSpace(target.Kind) || !NetworkObjectKinds.All.Contains(target.Kind))
        {
            throw new JsonException(
                $"'{prefix}.kind' value '{target.Kind ?? "null"}' is not a recognised network object kind.");
        }

        if (expectedKind is not null && !string.Equals(target.Kind, expectedKind, StringComparison.Ordinal))
        {
            throw new JsonException(
                $"'{prefix}.kind' value '{target.Kind}' does not match summary kind '{expectedKind}'.");
        }

        if (target.InterfacePath is not null && target.Kind != NetworkObjectKinds.Node)
            throw new JsonException($"'{prefix}.interfacePath' is only applicable to node targets.");

        switch (target.Kind)
        {
            case NetworkObjectKinds.DeviceItem:
                RequireSelectorText(target.DeviceName, $"{prefix}.deviceName", target.Kind);
                RequireSelectorPath(target, prefix, target.Kind);
                RejectSelectorFields(target, prefix, target.Kind,
                    interfaceFields: true, nodeField: true, subnetField: true,
                    numberField: true, ioSystemFields: true, connectionFields: true);
                break;

            case NetworkObjectKinds.NetworkInterface:
                RequireSelectorText(target.DeviceName, $"{prefix}.deviceName", target.Kind);
                RequireSelectorPath(target, prefix, target.Kind);
                RequireOptionalSelectorText(target.InterfaceName, $"{prefix}.interfaceName", target.Kind);
                RequireOptionalSelectorText(target.InterfaceType, $"{prefix}.interfaceType", target.Kind);
                RequireOptionalSelectorText(target.InterfaceOperatingMode, $"{prefix}.interfaceOperatingMode", target.Kind);
                RejectSelectorFields(target, prefix, target.Kind,
                    nodeField: true, subnetField: true, numberField: true, connectionFields: true);
                break;

            case NetworkObjectKinds.Node:
                RequireSelectorText(target.DeviceName, $"{prefix}.deviceName", target.Kind);
                RequireSelectorText(target.NodeId, $"{prefix}.nodeId", target.Kind);
                if (target.InterfacePath is not null && target.ItemPath is not null)
                    throw new JsonException($"'{prefix}.interfacePath' and '{prefix}.itemPath' cannot both be supplied.");
                if (target.InterfacePath is null && (target.ItemPath is null) != (target.NodeIndex is null))
                {
                    throw new JsonException(
                        $"'{prefix}.itemPath' and '{prefix}.nodeIndex' must be supplied together for kind '{target.Kind}'.");
                }
                if (target.ItemPath is not null)
                {
                    RequireSelectorPath(target, prefix, target.Kind);
                }
                if (target.InterfacePath is { } ownerPath)
                    ValidateInterfacePath(ownerPath, $"{prefix}.interfacePath");
                RequireOptionalSelectorText(target.InterfaceName, $"{prefix}.interfaceName", target.Kind);
                if (target.InterfaceName is not null && target.ItemPath is null && target.InterfacePath is null)
                    throw new JsonException($"'{prefix}.interfaceName' requires an owner path.");
                RejectSelectorField(target.InterfaceType is not null, prefix, "interfaceType", target.Kind);
                RejectSelectorField(target.InterfaceOperatingMode is not null, prefix, "interfaceOperatingMode", target.Kind);
                if (target.NodeIndex < 0)
                {
                    throw new JsonException($"'{prefix}.nodeIndex' must not be negative.");
                }
                RejectSelectorFields(target, prefix, target.Kind,
                    subnetField: true,
                    numberField: true, ioSystemFields: true, connectionFields: true);
                break;

            case NetworkObjectKinds.Subnet:
                RequireSelectorText(target.SubnetId, $"{prefix}.subnetId", target.Kind);
                RejectSelectorFields(target, prefix, target.Kind,
                    deviceField: true, itemPathField: true, interfaceFields: true,
                    nodeField: true, numberField: true, ioSystemFields: true, connectionFields: true);
                break;

            case NetworkObjectKinds.IoSystem:
                RequireSelectorText(target.SubnetId, $"{prefix}.subnetId", target.Kind);
                if (target.Number is null)
                {
                    throw new JsonException($"'{prefix}.number' is required for kind '{target.Kind}'.");
                }
                if (target.Number < 0)
                {
                    throw new JsonException($"'{prefix}.number' must not be negative.");
                }
                if (target.IoSystemIndex < 0)
                {
                    throw new JsonException($"'{prefix}.ioSystemIndex' must not be negative.");
                }
                RequireOptionalSelectorText(target.IoSystemName, $"{prefix}.ioSystemName", target.Kind);

                RejectSelectorFields(target, prefix, target.Kind,
                    deviceField: true, itemPathField: true, interfaceFields: true,
                    nodeField: true, connectionFields: true);
                break;

            case NetworkObjectKinds.CommunicationConnection:
                RequireSelectorText(target.DeviceName, $"{prefix}.deviceName", target.Kind);
                RequireSelectorPath(target, prefix, target.Kind);
                if (target.ConnectionIndex is null)
                {
                    throw new JsonException(
                        $"'{prefix}.connectionIndex' is required for kind '{target.Kind}'.");
                }
                if (target.ConnectionIndex < 0)
                {
                    throw new JsonException($"'{prefix}.connectionIndex' must not be negative.");
                }

                RequireSelectorText(target.ConnectionType, $"{prefix}.connectionType", target.Kind);
                if (!SupportedConnectionTypes.Contains(target.ConnectionType!))
                {
                    throw new JsonException(
                        $"'{prefix}.connectionType' value '{target.ConnectionType}' is not supported.");
                }
                RequireSelectorText(target.LocalConnectionName, $"{prefix}.localConnectionName", target.Kind);
                if (string.Equals(target.ConnectionType, "HmiConnection", StringComparison.Ordinal))
                {
                    if (target.LocalConnectionId is not null)
                    {
                        throw new JsonException(
                            $"'{prefix}.localConnectionId' is not applicable for connection type 'HmiConnection'.");
                    }
                }
                else
                {
                    RequireSelectorText(target.LocalConnectionId, $"{prefix}.localConnectionId", target.Kind);
                }
                RejectSelectorFields(target, prefix, target.Kind,
                    interfaceFields: true, nodeField: true, subnetField: true,
                    numberField: true, ioSystemFields: true);
                break;
        }
    }

    private static void RequireSelectorPath(NetworkObjectSelectorInfo target, string prefix, string kind)
    {
        if (target.ItemPath is null || target.ItemPath.Count == 0)
        {
            throw new JsonException($"'{prefix}.itemPath' must be non-empty for kind '{kind}'.");
        }

        foreach (var segment in target.ItemPath)
        {
            RequireNotNull(segment, $"{prefix}.itemPath[]");
            if (segment!.Index < 0)
            {
                throw new JsonException($"'{prefix}.itemPath[].index' must not be negative.");
            }

            RequireSelectorText(segment.Name, $"{prefix}.itemPath[].name", kind);
            if (segment.PositionNumber < 0)
            {
                throw new JsonException($"'{prefix}.itemPath[].positionNumber' must not be negative.");
            }

            RequireSelectorText(segment.TypeIdentifier, $"{prefix}.itemPath[].typeIdentifier", kind);
        }
    }

    private static void RequireSelectorText(string? value, string member, string kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException($"'{member}' is required for kind '{kind}'.");
        }
    }

    private static void RequireOptionalSelectorText(string? value, string member, string kind)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException($"'{member}' must be nonblank when supplied for kind '{kind}'.");
        }
    }

    private static void RejectSelectorFields(
        NetworkObjectSelectorInfo target,
        string prefix,
        string kind,
        bool deviceField = false,
        bool itemPathField = false,
        bool interfaceFields = false,
        bool nodeField = false,
        bool subnetField = false,
        bool numberField = false,
        bool ioSystemFields = false,
        bool connectionFields = false)
    {
        RejectSelectorField(deviceField && target.DeviceName is not null, prefix, "deviceName", kind);
        RejectSelectorField(itemPathField && target.ItemPath is not null, prefix, "itemPath", kind);
        RejectSelectorField(interfaceFields && target.InterfaceName is not null, prefix, "interfaceName", kind);
        RejectSelectorField(interfaceFields && target.InterfaceType is not null, prefix, "interfaceType", kind);
        RejectSelectorField(interfaceFields && target.InterfaceOperatingMode is not null, prefix, "interfaceOperatingMode", kind);
        RejectSelectorField(nodeField && target.NodeId is not null, prefix, "nodeId", kind);
        RejectSelectorField(nodeField && target.NodeIndex is not null, prefix, "nodeIndex", kind);
        RejectSelectorField(subnetField && target.SubnetId is not null, prefix, "subnetId", kind);
        RejectSelectorField(numberField && target.Number is not null, prefix, "number", kind);
        RejectSelectorField(ioSystemFields && target.IoSystemIndex is not null, prefix, "ioSystemIndex", kind);
        RejectSelectorField(ioSystemFields && target.IoSystemName is not null, prefix, "ioSystemName", kind);
        RejectSelectorField(connectionFields && target.ConnectionIndex is not null, prefix, "connectionIndex", kind);
        RejectSelectorField(connectionFields && target.ConnectionType is not null, prefix, "connectionType", kind);
        RejectSelectorField(connectionFields && target.LocalConnectionName is not null, prefix, "localConnectionName", kind);
        RejectSelectorField(connectionFields && target.LocalConnectionId is not null, prefix, "localConnectionId", kind);
    }

    private static void RejectSelectorField(bool isPresent, string prefix, string member, string kind)
    {
        if (isPresent)
        {
            throw new JsonException($"'{prefix}.{member}' is not applicable for kind '{kind}'.");
        }
    }

    private static void ValidateAttributeValue(NetworkAttributeValueInfo value, string prefix)
    {
        if (string.Equals(value.Kind, "null", StringComparison.Ordinal))
        {
            if (value.Value is null
                || value.Value is JsonElement { ValueKind: JsonValueKind.Null })
            {
                return;
            }

            throw new JsonException(
                $"'{prefix}.value.value' does not match kind 'null'.");
        }

        if (value.Value is not JsonElement element)
        {
            throw new JsonException($"'{prefix}.value.value' is not a JSON value.");
        }

        var matchesKind = value.Kind switch
        {
            "string" => element.ValueKind == JsonValueKind.String,
            "boolean" => element.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out _),
            "number" => element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out _),
            "enum" => ValidateEnumValue(element),
            _ => false,
        };

        if (!matchesKind)
        {
            throw new JsonException(
                $"'{prefix}.value.value' does not match kind '{value.Kind}'.");
        }
    }

    private static bool ValidateEnumValue(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        _ = CanonicalJson.DeserializeWorkerPayload<NetworkEnumValueInfo>(element.GetRawText());
        return true;
    }

    private static void RequireNotNull(object? value, string member)
    {
        if (value is null)
        {
            throw new JsonException($"'{member}' is declared non-nullable but the payload was null.");
        }
    }

    private static StructuredOperationItem Failed(
        NetworkOperationRequest operation,
        string category,
        string message,
        IReadOnlyList<string> warnings)
        => new(
            operation.OperationId,
            operation.Operation,
            OperationBatchStatus.Failed,
            Result: null,
            new StructuredOperationFailure(category, message),
            Omission: null,
            SkipReason: null,
            warnings);
}
