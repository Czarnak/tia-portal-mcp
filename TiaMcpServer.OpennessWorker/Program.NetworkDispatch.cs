using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Network;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

internal static partial class Program
{
    // Domain validation stays before the shared session gates, which remain owned by Program.
    private static class NetworkWorkerDispatch
    {
        public static WorkerResponse ReadHardwareConfig(WorkerRequest request)
        {
            return WithEngineeringRoot(request, project => Success(HardwareConfigReader.Read(
                project,
                request.DeviceName,
                request.PlcName,
                request.IncludeIoDetails,
                request.IncludeTagMatches)));
        }

        public static WorkerResponse ReadHardwarePageCandidates(WorkerRequest request)
        {
            return WithEngineeringRoot(request, project =>
            {
                ValidateHardwarePageContinuationIdentity(request);
                var source = HardwarePageCandidateSourceFactory.Create(
                    project,
                    request.DeviceName,
                    request.PlcName,
                    request.IncludeIoDetails,
                    request.IncludeTagMatches);
                return Success(HardwarePageCandidateReader.Read(
                    source,
                    request.DeviceName,
                    request.PlcName,
                    request.IncludeIoDetails,
                    request.IncludeTagMatches,
                    request.HardwarePageSize,
                    request.HardwarePageContinuation));
            });
        }

        public static WorkerResponse ListNetworkObjects(WorkerRequest request)
        {
            ValidateListNetworkObjectsRequest(request);

            int pageSize;
            try
            {
                pageSize = NetworkObjectPageBuilder.ResolvePageSize(request.NetworkObjectPageSize);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "NetworkObjectPageSize must be between 1 and 200.");
            }

            return WithEngineeringRoot(request, project =>
            {
                var orderedItems = NetworkObjectIndexReader.Read(project, request.NetworkObjectKinds!, request.NetworkObjectDeviceName);
                var queryHash = NetworkObjectCursorCodec.CreateQueryHash(request.NetworkObjectKinds!, request.NetworkObjectDeviceName);
                var snapshotHash = NetworkObjectCursorCodec.CreateSnapshotHash(orderedItems);
                var offset = request.NetworkObjectCursor is null
                    ? 0
                    : NetworkObjectCursorCodec.Decode(
                        request.NetworkObjectCursor,
                        queryHash,
                        snapshotHash,
                        orderedItems.Count).Offset;
                return Success(NetworkObjectPageBuilder.Build(orderedItems, pageSize, offset, queryHash, snapshotHash));
            });
        }

        private static void ValidateListNetworkObjectsRequest(WorkerRequest request)
        {
            if (request.NetworkObjectKinds is null || request.NetworkObjectKinds.Count == 0)
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "NetworkObjectKinds is required.");
            }

            if (request.NetworkObjectKinds.Any(kind => !NetworkObjectKinds.All.Contains(kind))
                || request.NetworkObjectKinds.Distinct(StringComparer.Ordinal).Count() != request.NetworkObjectKinds.Count)
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "NetworkObjectKinds is invalid.");
            }

            if (request.NetworkObjectDeviceName is not null && string.IsNullOrWhiteSpace(request.NetworkObjectDeviceName))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "NetworkObjectDeviceName must not be blank.");
            }

            if (request.NetworkObjectDeviceName is not null
                && (request.NetworkObjectKinds.Contains(NetworkObjectKinds.Subnet)
                    || request.NetworkObjectKinds.Contains(NetworkObjectKinds.IoSystem)))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.ValidationError,
                    "NetworkObjectDeviceName is not applicable to subnet or ioSystem discovery.");
            }
        }

        public static WorkerResponse InspectNetworkObject(WorkerRequest request)
        {
            if (request.NetworkObjectTarget is null)
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.ValidationError,
                    "NetworkObjectTarget is required.");
            }

            return WithEngineeringRoot(request, project =>
            {
                var resolution = NetworkObjectSelectorResolver.Resolve(project, request.NetworkObjectTarget);
                if (!resolution.Success)
                {
                    return Failure(
                        resolution.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                        resolution.Error ?? "The network object target could not be resolved.");
                }

                return Success(NetworkObjectInspector.Inspect(
                    resolution.Resolved!,
                    request.NetworkAttributeNames));
            });
        }

        public static WorkerResponse SearchEquipmentCatalog(WorkerRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Query is required.");
            }

            return WithSession(request, session =>
            {
                var failure = EnsureRequestedProjectOpen(session, request.ProjectPath);
                if (failure is not null)
                {
                    return failure;
                }
                ValidateExpectedAfterProjectResolution(session, request);

                if (session.TiaPortal is null)
                {
                    return Failure(
                        WorkerFailureCategories.WorkerOperationFailed,
                        "No TIA Portal session is connected. Please start TIA Portal and try again.");
                }

                return Success(EquipmentCatalogSearcher.Search(session.TiaPortal, request.Query!, request.MaxResults));
            });
        }

        public static WorkerResponse AddNetworkDevice(WorkerRequest request)
        {
            if (!CatalogTypeIdentifier.IsCreatable(request.TypeIdentifier))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.ValidationError,
                    CatalogTypeIdentifier.BuildValidationMessage(request.TypeIdentifier));
            }

            if (string.IsNullOrWhiteSpace(request.DeviceName))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "DeviceName is required.");
            }

            return WithEngineeringRoot(request, project =>
            {
                var result = NetworkDeviceCreator.Create(project, request.TypeIdentifier!, request.DeviceName!,
                    string.IsNullOrWhiteSpace(request.DeviceItemName) ? request.DeviceName! : request.DeviceItemName!);
                return Success(result);
            });
        }

        public static WorkerResponse ConfigureNetworkDevice(WorkerRequest request)
        {
            var target = NetworkConfigurationTargetBinding.Resolve(request);

            return WithEngineeringRoot(request, project =>
            {
                // Capture the normalized owner while retaining supplied constraints for the final
                // pre-setter resolver.
                var prepared = NetworkObjectSelectorResolver.ResolveNode(project, target);
                if (!prepared.Success) throw new WorkerOperationException(prepared.FailureCategory!, prepared.Error!);
                var dispatchTarget = target.InterfacePath is not null || target.ItemPath is not null ? target : prepared.Resolved!.Target;
                dispatchTarget.DeviceName = target.DeviceName;
                dispatchTarget.InterfaceName = target.InterfaceName;
                dispatchTarget.NodeIndex = target.NodeIndex;
                var result = NetworkDeviceConfigurator.Configure(
                    project,
                    dispatchTarget,
                    request.IpAddress,
                    request.SubnetMask,
                    request.PnDeviceName,
                    request.PnDeviceNameAutoGeneration,
                    request.SubnetId,
                    request.IoSystemSubnetId,
                    request.IoSystemNumber);
                return Success(result);
            });
        }

        public static WorkerResponse CreateSubnet(WorkerRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SubnetName))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "SubnetName is required.");
            }

            if (!SubnetLifecycleContract.IsSupportedNetworkType(request.SubnetNetworkType))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.ValidationError,
                    "SubnetNetworkType is required. Valid values: "
                    + $"{SubnetLifecycleContract.Ethernet}, {SubnetLifecycleContract.Profibus}.");
            }

            if (string.Equals(request.SubnetNetworkType, SubnetLifecycleContract.Ethernet, StringComparison.Ordinal)
                && (request.SubnetHighestAddress is not null || request.SubnetTransmissionSpeed is not null))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.ValidationError,
                    $"SubnetHighestAddress and SubnetTransmissionSpeed are not applicable for network type "
                    + $"'{SubnetLifecycleContract.Ethernet}'.");
            }

            ValidateSubnetHighestAddressRange(request.SubnetHighestAddress);
            ValidateSubnetTransmissionSpeedValue(request.SubnetTransmissionSpeed);

            return WithActiveContext(request, session => Success(SubnetLifecycleService.Create(
                session.TiaPortal!,
                session.EngineeringRoot!,
                request.SubnetName!,
                request.SubnetNetworkType!,
                request.SubnetHighestAddress,
                request.SubnetTransmissionSpeed)));
        }

        public static WorkerResponse UpdateSubnet(WorkerRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SubnetId))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "SubnetId is required.");
            }

            if (request.SubnetName is not null && string.IsNullOrWhiteSpace(request.SubnetName))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "SubnetName must not be blank when supplied.");
            }

            if (request.SubnetName is null && request.SubnetHighestAddress is null && request.SubnetTransmissionSpeed is null)
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.ValidationError,
                    "update_subnet requires at least one requested change (name, highestAddress, or transmissionSpeed).");
            }

            ValidateSubnetHighestAddressRange(request.SubnetHighestAddress);
            ValidateSubnetTransmissionSpeedValue(request.SubnetTransmissionSpeed);

            return WithActiveContext(request, session => Success(SubnetLifecycleService.Update(
                session.TiaPortal!,
                session.EngineeringRoot!,
                request.SubnetId!,
                request.SubnetName,
                request.SubnetHighestAddress,
                request.SubnetTransmissionSpeed)));
        }

        public static WorkerResponse DeleteSubnet(WorkerRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SubnetId))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "SubnetId is required.");
            }

            return WithActiveContext(request, session => Success(SubnetLifecycleService.Delete(
                session.TiaPortal!,
                session.EngineeringRoot!,
                request.SubnetId!)));
        }

        private static void ValidateSubnetHighestAddressRange(int? value)
        {
            if (value is { } address
                && (address < SubnetLifecycleContract.MinimumHighestAddress || address > SubnetLifecycleContract.MaximumHighestAddress))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.ValidationError,
                    $"SubnetHighestAddress must be between {SubnetLifecycleContract.MinimumHighestAddress} and "
                    + $"{SubnetLifecycleContract.MaximumHighestAddress} (received {address}).");
            }
        }

        private static void ValidateSubnetTransmissionSpeedValue(string? value)
        {
            if (value is not null && !SubnetLifecycleContract.IsSupportedTransmissionSpeed(value))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.ValidationError,
                    $"SubnetTransmissionSpeed value '{value}' is not supported. Valid values: "
                    + $"{string.Join(", ", SubnetLifecycleContract.TransmissionSpeeds)}.");
            }
        }
    }
}
