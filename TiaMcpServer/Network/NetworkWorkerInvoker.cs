using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

/// <summary>Dispatches validated dedicated network operations to the existing worker client.</summary>
public static class NetworkWorkerInvoker
{
    public static Task<WorkerCallResult> InvokeReadAsync(
        OpennessWorkerClient client,
        NetworkOperationRequest operation) => operation.Operation switch
        {
            "read_hardware_config" => client.ReadHardwareConfigAsync(operation.ProjectPath),
            "search_equipment_catalog" => client.SearchEquipmentCatalogAsync(
                operation.Query!, operation.ProjectPath, operation.MaxResults),
            "list_network_objects" => client.ListNetworkObjectsAsync(
                operation.ObjectKinds!,
                operation.DeviceName,
                operation.PageSize,
                operation.Cursor,
                operation.ProjectPath),
            "inspect_network_object" => client.InspectNetworkObjectAsync(
                MapSelector(operation.Target!),
                operation.AttributeNames,
                operation.ProjectPath),
            _ => Task.FromResult(WorkerCallResult.Fail(
                WorkerFailureCategories.ValidationError,
                $"Unsupported network read operation '{operation.Operation}'.")),
        };

    /// <summary>
    /// Maps the host's JSON-decorated <see cref="NetworkObjectTarget"/> to a fresh
    /// <see cref="NetworkObjectSelectorInfo"/> for the worker protocol. Every item-path segment
    /// is deep-copied so the worker request never holds a reference to the caller's mutable list.
    /// </summary>
    private static NetworkObjectSelectorInfo MapSelector(NetworkObjectTarget target) =>
        new NetworkObjectSelectorInfo
        {
            Kind = target.Kind,
            DeviceName = target.DeviceName,
            ItemPath = target.ItemPath is null
                ? null
                : target.ItemPath
                    .Select(segment => new DeviceItemPathSegmentInfo
                    {
                        Index = segment.Index
                            ?? throw new InvalidOperationException(
                                "Validated network selector path segment is missing index."),
                        Name = segment.Name,
                        PositionNumber = segment.PositionNumber
                            ?? throw new InvalidOperationException(
                                "Validated network selector path segment is missing positionNumber."),
                        TypeIdentifier = segment.TypeIdentifier,
                    })
                    .ToList(),
            InterfaceName = target.InterfaceName,
            InterfaceType = target.InterfaceType,
            InterfaceOperatingMode = target.InterfaceOperatingMode,
            NodeId = target.NodeId,
            NodeIndex = target.NodeIndex,
            SubnetId = target.SubnetId,
            Number = target.Number,
            IoSystemIndex = target.IoSystemIndex,
            IoSystemName = target.IoSystemName,
            ConnectionIndex = target.ConnectionIndex,
            ConnectionType = target.ConnectionType,
            LocalConnectionName = target.LocalConnectionName,
            LocalConnectionId = target.LocalConnectionId,
        };

    public static Task<WorkerCallResult> InvokeWriteAsync(
        OpennessWorkerClient client,
        NetworkOperationRequest operation,
        string? commonProjectPath)
    {
        var projectPath = commonProjectPath ?? operation.ProjectPath;
        return operation.Operation switch
        {
            "add_network_device" => client.AddNetworkDeviceAsync(
                operation.TypeIdentifier!,
                operation.DeviceName!,
                operation.DeviceItemName ?? operation.DeviceName!,
                projectPath),
            "configure_network_device" => client.ConfigureNetworkDeviceAsync(
                operation.Target!.DeviceName!,
                operation.Target!.NodeId!,
                operation.Changes!.IpAddress,
                operation.Changes!.SubnetMask,
                operation.Changes!.PnDeviceName,
                operation.Changes!.Subnet?.SubnetId,
                operation.Changes!.IoSystem?.SubnetId,
                operation.Changes!.IoSystem?.Number,
                projectPath),
            _ => Task.FromResult(WorkerCallResult.Fail(
                WorkerFailureCategories.ValidationError,
                $"Unsupported network write operation '{operation.Operation}'.")),
        };
    }
}
