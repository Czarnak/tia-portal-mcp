using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Network;

/// <summary>
/// Flat, strict request shape for one dedicated network operation. Only fields declared by the
/// selected operation are permitted by <see cref="NetworkOperationCatalog"/>.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkOperationRequest : IOperationBatchItem
{
    [Description("Client-supplied unique identifier for this network operation; returned results are keyed by it.")]
    public string OperationId { get; set; } = string.Empty;

    [Description("Network operation to run: read_hardware_config, search_equipment_catalog, add_network_device, or configure_network_device.")]
    public string Operation { get; set; } = string.Empty;

    [Description("Optional absolute project path (.ap21). When omitted, the active project is used; all network writes in one request must share it.")]
    public string? ProjectPath { get; set; }

    [Description("Hardware catalog search query. Required by search_equipment_catalog.")]
    public string? Query { get; set; }

    [Description("Optional result cap for search_equipment_catalog; must be 1 or greater when supplied.")]
    public int? MaxResults { get; set; }

    [Description("Exact equipment catalog type identifier. Required by add_network_device.")]
    public string? TypeIdentifier { get; set; }

    [Description("Network device name. Required by add_network_device and configure_network_device.")]
    public string? DeviceName { get; set; }

    [Description("Optional device item name for add_network_device; defaults to deviceName when omitted.")]
    public string? DeviceItemName { get; set; }

    [Description("Optional IP address for configure_network_device.")]
    public string? IpAddress { get; set; }

    [Description("Optional subnet mask for configure_network_device.")]
    public string? SubnetMask { get; set; }

    [Description("Optional PROFINET device name for configure_network_device.")]
    public string? PnDeviceName { get; set; }

    [Description("Optional subnet name for configure_network_device.")]
    public string? SubnetName { get; set; }

    [Description("Optional IO-system name for configure_network_device.")]
    public string? IoSystemName { get; set; }
}
