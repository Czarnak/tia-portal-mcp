namespace TiaMcpServer.Batch;

/// <summary>
/// Flat request shape for a single item inside a batch operation. Fields mirror the
/// scalar parameters of the existing single MCP tools; only the fields relevant to the
/// chosen <see cref="Operation"/> are read.
/// </summary>
public sealed class BatchOperationRequest
{
    /// <summary>Client-supplied unique identifier for this item within the batch.</summary>
    public string OperationId { get; set; } = string.Empty;

    /// <summary>Existing tool name, e.g. get_block_content, update_block_logic, create_tag.</summary>
    public string Operation { get; set; } = string.Empty;

    public string? ProjectPath { get; set; }

    public string? BlockPath { get; set; }

    public string? YamlContent { get; set; }

    public string? PlcName { get; set; }

    public string? Query { get; set; }

    public string? Filter { get; set; }

    public string? TableName { get; set; }

    public string? FolderPath { get; set; }

    public string? Name { get; set; }

    public string? NewName { get; set; }

    public string? DataType { get; set; }

    public string? LogicalAddress { get; set; }

    public string? Value { get; set; }

    public bool? ExternalAccessible { get; set; }

    public bool? ExternalVisible { get; set; }

    public bool? ExternalWritable { get; set; }

    public bool? IsSafety { get; set; }

    public string? TypeIdentifier { get; set; }

    public string? DeviceName { get; set; }

    public string? DeviceItemName { get; set; }

    public string? IpAddress { get; set; }

    public string? SubnetMask { get; set; }

    public string? PnDeviceName { get; set; }

    public string? SubnetName { get; set; }

    public string? IoSystemName { get; set; }
}
