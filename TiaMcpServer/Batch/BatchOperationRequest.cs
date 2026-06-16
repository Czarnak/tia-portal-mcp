using System.ComponentModel;

namespace TiaMcpServer.Batch;

/// <summary>
/// Flat request shape for a single item inside a batch operation. Fields mirror the
/// scalar parameters of the existing single MCP tools; only the fields relevant to the
/// chosen <see cref="Operation"/> are read.
/// </summary>
public sealed class BatchOperationRequest
{
    [Description("Client-supplied unique identifier for this item within the batch; the result is keyed by it.")]
    public string OperationId { get; set; } = string.Empty;

    [Description("The operation to run for this item. Read operations: browse_project_tree, read_hardware_config, read_cross_references, search_equipment_catalog, get_block_content, list_tag_tables, compile_check, get_project_status. "
        + "Write operations: update_block_logic, create_tag_table, delete_tag_table, create_tag, update_tag, delete_tag, create_user_constant, update_user_constant, delete_user_constant, add_network_device, configure_network_device. "
        + "Use reads only in execute_read_batch and writes only in preview_write_batch/apply_write_batch.")]
    public string Operation { get; set; } = string.Empty;

    [Description("Optional absolute project path (.ap21). When omitted, the active project is used; all write items in one batch must share the same project path.")]
    public string? ProjectPath { get; set; }

    [Description("PLC block path, e.g. PLC_1/Main or PLC_1/Blocks/Folder/Block. Required by get_block_content and update_block_logic.")]
    public string? BlockPath { get; set; }

    [Description("SIMATIC SD document content for the block. Required by update_block_logic.")]
    public string? YamlContent { get; set; }

    [Description("Optional PLC software name used to scope the operation.")]
    public string? PlcName { get; set; }

    [Description("Hardware catalog search query. Required by search_equipment_catalog.")]
    public string? Query { get; set; }

    [Description("Optional filter, e.g. a cross-reference filter such as ObjectsWithReferences or UnusedObjects.")]
    public string? Filter { get; set; }

    [Description("PLC tag table name. Required by the tag, tag-table, and user-constant operations.")]
    public string? TableName { get; set; }

    [Description("Optional folder path for the tag table.")]
    public string? FolderPath { get; set; }

    [Description("Tag or user-constant name. Required by create/update/delete tag and user-constant operations.")]
    public string? Name { get; set; }

    [Description("Optional new name when renaming a tag or user constant.")]
    public string? NewName { get; set; }

    [Description("Data type, e.g. Bool or Int. Required by create_tag and create_user_constant.")]
    public string? DataType { get; set; }

    [Description("Optional logical address, e.g. %I0.0.")]
    public string? LogicalAddress { get; set; }

    [Description("Constant value. Required by create_user_constant.")]
    public string? Value { get; set; }

    [Description("Optional tag attribute: external accessibility.")]
    public bool? ExternalAccessible { get; set; }

    [Description("Optional tag attribute: external visibility.")]
    public bool? ExternalVisible { get; set; }

    [Description("Optional tag attribute: external writability.")]
    public bool? ExternalWritable { get; set; }

    [Description("Optional flag marking the tag as a safety tag.")]
    public bool? IsSafety { get; set; }

    [Description("Exact catalog type identifier, e.g. OrderNumber:6ES7 510-1DJ01-0AB0/V2.0. Required by add_network_device.")]
    public string? TypeIdentifier { get; set; }

    [Description("Device name. Required by add_network_device and configure_network_device.")]
    public string? DeviceName { get; set; }

    [Description("Optional device item name; defaults to deviceName when omitted.")]
    public string? DeviceItemName { get; set; }

    [Description("Optional IP address for configure_network_device, e.g. 192.168.0.10.")]
    public string? IpAddress { get; set; }

    [Description("Optional subnet mask for configure_network_device, e.g. 255.255.255.0.")]
    public string? SubnetMask { get; set; }

    [Description("Optional PROFINET device name for configure_network_device.")]
    public string? PnDeviceName { get; set; }

    [Description("Optional subnet name for configure_network_device.")]
    public string? SubnetName { get; set; }

    [Description("Optional IO-system name for configure_network_device.")]
    public string? IoSystemName { get; set; }
}
