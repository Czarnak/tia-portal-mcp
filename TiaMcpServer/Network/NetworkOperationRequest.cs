using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Network;

/// <summary>
/// Strict request shape for one dedicated network operation. Only fields declared by the selected
/// operation are permitted by <see cref="NetworkOperationCatalog"/>.
///
/// <para>
/// Creation (<c>add_network_device</c>) stays flat because it names something that does not exist
/// yet. Configuration (<c>configure_network_device</c>) instead splits into an exact
/// <see cref="Target"/> — which existing object is being written — and <see cref="Changes"/> —
/// what to set on it. A null change member means "leave this alone"; there is no flat alias and no
/// compatibility converter, so a caller that still sends the legacy flat fields is rejected rather
/// than silently writing to a device-wide guess.
/// </para>
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

    [Description("Name for the new network device. Required by add_network_device.")]
    public string? DeviceName { get; set; }

    [Description("Optional device item name for add_network_device; defaults to deviceName when omitted.")]
    public string? DeviceItemName { get; set; }

    [Description("Exact existing object to configure. Required by configure_network_device.")]
    public NetworkDeviceTarget? Target { get; set; }

    [Description("Settings to change on the target. Required by configure_network_device; at least one change must be requested.")]
    public NetworkDeviceChanges? Changes { get; set; }
}

/// <summary>Names exactly one existing node on exactly one existing device.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkDeviceTarget
{
    [Description("Exact name of the existing device to configure.")]
    public string? DeviceName { get; init; }

    [Description("Exact nodeId reported by read_hardware_config for the node to configure. Required because a device may expose several interfaces and nodes.")]
    public string? NodeId { get; init; }
}

/// <summary>What to set on the targeted node. Every member is optional; null means no change.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkDeviceChanges
{
    [Description("New IP address for the targeted node. Omit to leave it unchanged.")]
    public string? IpAddress { get; init; }

    [Description("New subnet mask for the targeted node. Omit to leave it unchanged.")]
    public string? SubnetMask { get; init; }

    [Description("New PROFINET device name for the targeted node. Omit to leave it unchanged.")]
    public string? PnDeviceName { get; init; }

    [Description("Subnet to connect the targeted node to. Omit to leave the connection unchanged.")]
    public NetworkSubnetTarget? Subnet { get; init; }

    [Description("IO system to attach the targeted node to. Omit to leave the attachment unchanged.")]
    public NetworkIoSystemTarget? IoSystem { get; init; }
}

/// <summary>Names exactly one existing subnet.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkSubnetTarget
{
    [Description("Exact subnetId reported by read_hardware_config for the subnet to connect to.")]
    public string? SubnetId { get; init; }
}

/// <summary>Names exactly one existing IO system by its subnet and its number within that subnet.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkIoSystemTarget
{
    [Description("Exact subnetId of the subnet that owns the IO system. Must match changes.subnet.subnetId when a subnet change is also requested.")]
    public string? SubnetId { get; init; }

    [Description("IO system number within that subnet, as reported by read_hardware_config.")]
    public int? Number { get; init; }
}
