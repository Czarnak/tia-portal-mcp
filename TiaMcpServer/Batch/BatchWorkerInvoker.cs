using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Batch;

/// <summary>
/// Maps validated batch items to existing <see cref="OpennessWorkerClient"/> calls. This is the
/// only worker-coupled part of the batch layer; orchestration and validation live elsewhere.
/// Required fields are guaranteed present by <see cref="BatchOperationCatalog"/> before this runs.
/// </summary>
public static class BatchWorkerInvoker
{
    /// <summary>Reads the current state a write item's safety token binds to.</summary>
    public static Task<WorkerCallResult> ReadCurrentStateAsync(OpennessWorkerClient client, BatchOperationRequest op) => op.Operation switch
    {
        "update_block_logic" => client.GetBlockContentAsync(op.BlockPath!, op.ProjectPath),
        "create_tag_table" or "delete_tag_table"
            or "create_tag" or "update_tag" or "delete_tag"
            or "create_user_constant" or "update_user_constant" or "delete_user_constant"
            => client.ListTagTablesAsync(op.PlcName, op.ProjectPath),
        "add_network_device" or "configure_network_device"
            => client.ReadHardwareConfigAsync(op.ProjectPath),
        "create_block" or "create_block_group" or "delete_block_group"
            => client.BrowseProjectTreeAsync(op.ProjectPath),
        "delete_block"
            => client.GetBlockContentAsync(op.BlockPath!, op.ProjectPath),
        "start_plc" or "stop_plc"
            => client.GetProjectStatusAsync(op.ProjectPath),
        _ => Task.FromResult(WorkerCallResult.Fail(
            WorkerFailureCategories.ValidationError,
            $"Unsupported batch write operation '{op.Operation}'.")),
    };

    /// <summary>Executes a single read or write item against the worker.</summary>
    public static Task<WorkerCallResult> InvokeAsync(OpennessWorkerClient client, BatchOperationRequest op) => op.Operation switch
    {
        // Reads
        "browse_project_tree" => client.BrowseProjectTreeAsync(op.ProjectPath, op.Depth, op.StartPath),
        "read_hardware_config" => client.ReadHardwareConfigAsync(op.ProjectPath),
        "search_equipment_catalog" => client.SearchEquipmentCatalogAsync(op.Query!, op.ProjectPath, op.MaxResults),
        "read_cross_references" => client.ReadCrossReferencesAsync(op.ProjectPath, op.PlcName, op.Filter, op.MaxResults),
        "get_block_content" => client.GetBlockContentAsync(op.BlockPath!, op.ProjectPath),
        "list_tag_tables" => client.ListTagTablesAsync(op.PlcName, op.ProjectPath),
        "compile_check" => client.CompileCheckAsync(op.BlockPath, op.PlcName, op.ProjectPath),
        "get_project_status" => client.GetProjectStatusAsync(op.ProjectPath),

        // Data writes
        "update_block_logic" => client.UpdateBlockLogicAsync(op.BlockPath!, op.YamlContent!, op.ProjectPath),
        "create_tag_table" => client.CreateTagTableAsync(op.PlcName, op.TableName!, op.FolderPath, op.ProjectPath),
        "delete_tag_table" => client.DeleteTagTableAsync(op.PlcName, op.TableName!, op.FolderPath, op.ProjectPath),
        "create_tag" => client.CreateTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.DataType!, op.LogicalAddress, op.ProjectPath),
        "update_tag" => client.UpdateTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.NewName, op.DataType, op.LogicalAddress, op.ExternalAccessible, op.ExternalVisible, op.ExternalWritable, op.IsSafety, op.ProjectPath),
        "delete_tag" => client.DeleteTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.ProjectPath),
        "create_user_constant" => client.CreateUserConstantAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.DataType!, op.Value!, op.ProjectPath),
        "update_user_constant" => client.UpdateUserConstantAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.DataType, op.Value, op.ProjectPath),
        "delete_user_constant" => client.DeleteUserConstantAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.ProjectPath),
        "add_network_device" => client.AddNetworkDeviceAsync(op.TypeIdentifier!, op.DeviceName!, ResolveDeviceItemName(op), op.ProjectPath),
        "configure_network_device" => client.ConfigureNetworkDeviceAsync(op.DeviceName!, op.IpAddress, op.SubnetMask, op.PnDeviceName, op.SubnetName, op.IoSystemName, op.ProjectPath),
        "create_block" => client.CreateBlockAsync(op.BlockPath!, op.BlockType!, op.Language, op.ObEventClass, op.ProjectPath),
        "delete_block" => client.DeleteBlockAsync(op.BlockPath!, op.ProjectPath),
        "create_block_group" => client.CreateBlockGroupAsync(op.BlockPath!, op.ProjectPath),
        "delete_block_group" => client.DeleteBlockGroupAsync(op.BlockPath!, op.ProjectPath),
        "start_plc" => client.StartPlcAsync(op.PlcName, op.ProjectPath),
        "stop_plc" => client.StopPlcAsync(op.PlcName, op.ProjectPath),

        _ => Task.FromResult(WorkerCallResult.Fail(
            WorkerFailureCategories.ValidationError,
            $"Unsupported batch operation '{op.Operation}'.")),
    };

    private static string ResolveDeviceItemName(BatchOperationRequest op)
        => string.IsNullOrWhiteSpace(op.DeviceItemName) ? op.DeviceName! : op.DeviceItemName!;
}
