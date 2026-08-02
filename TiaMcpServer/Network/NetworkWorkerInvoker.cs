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
            _ => Task.FromResult(WorkerCallResult.Fail(
                WorkerFailureCategories.ValidationError,
                $"Unsupported network read operation '{operation.Operation}'.")),
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
