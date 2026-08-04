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
///
/// <para>
/// Phase 3 introspection adds two read operations: <c>list_network_objects</c> (paged discovery
/// driven by <see cref="ObjectKinds"/>) and <c>inspect_network_object</c> (attribute-level detail
/// driven by <see cref="Target"/>). The <see cref="Target"/> type is the same for both phases;
/// the selector shape enforced by the catalog differs by operation.
/// </para>
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkOperationRequest : IOperationBatchItem
{
    [Description("Client-supplied unique identifier for this network operation; returned results are keyed by it.")]
    public string OperationId { get; set; } = string.Empty;

    [Description("Network operation to run: read_hardware_config, search_equipment_catalog, add_network_device, configure_network_device, list_network_objects, or inspect_network_object.")]
    public string Operation { get; set; } = string.Empty;

    [Description("Optional absolute project path (.ap21). When omitted, the active project is used; all network writes in one request must share it.")]
    public string? ProjectPath { get; set; }

    [Description("Hardware catalog search query. Required by search_equipment_catalog.")]
    public string? Query { get; set; }

    [Description("Optional result cap for search_equipment_catalog; must be 1 or greater when supplied.")]
    public int? MaxResults { get; set; }

    [Description("Exact equipment catalog type identifier. Required by add_network_device.")]
    public string? TypeIdentifier { get; set; }

    [Description("Name for the new network device or filter for list_network_objects. Required by add_network_device; optional filter for list_network_objects.")]
    public string? DeviceName { get; set; }

    [Description("Optional device item name for add_network_device; defaults to deviceName when omitted.")]
    public string? DeviceItemName { get; set; }

    [Description("Exact existing object to configure or inspect. Required by configure_network_device and inspect_network_object.")]
    public NetworkObjectTarget? Target { get; set; }

    [Description("Settings to change on the target. Required by configure_network_device; at least one change must be requested.")]
    public NetworkDeviceChanges? Changes { get; set; }

    // ------------------------------------------------------------------
    // Phase 3 introspection fields
    // ------------------------------------------------------------------

    [Description("One or more network object kinds to enumerate. Required by list_network_objects. Valid values: deviceItem, networkInterface, node, subnet, ioSystem, communicationConnection.")]
    public IReadOnlyList<string>? ObjectKinds { get; set; }

    [Description("Maximum number of objects to return in one page (1–200). Optional for list_network_objects.")]
    public int? PageSize { get; set; }

    [Description("Opaque pagination cursor returned by a previous list_network_objects call. Optional for list_network_objects.")]
    public string? Cursor { get; set; }

    [Description("Attribute names to read on the inspected object. Optional for inspect_network_object; must be non-empty when supplied and must contain at most 200 unique names.")]
    public IReadOnlyList<string>? AttributeNames { get; set; }
}

/// <summary>
/// Names exactly one existing network object. Used both by configuration (Phase 2) and
/// by introspection (Phase 3). The <c>kind</c> field selects the selector shape; which
/// additional fields are required and which are inapplicable is enforced by the catalog.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkObjectTarget
{
    [Description("Network object kind. One of: deviceItem, networkInterface, node, subnet, ioSystem, communicationConnection. For configure_network_device, only 'node' or absent is accepted.")]
    public string? Kind { get; set; }

    [Description("Exact device name. Required for deviceItem, networkInterface, node, and communicationConnection kinds.")]
    public string? DeviceName { get; set; }

    [Description("Path through the device item hierarchy. Required for deviceItem kind.")]
    public IReadOnlyList<NetworkDeviceItemPathSegment>? ItemPath { get; set; }

    [Description("Network interface name. Required for networkInterface kind.")]
    public string? InterfaceName { get; set; }

    [Description("Network interface type (e.g. PROFINET, PROFIBUS). Optional for networkInterface kind.")]
    public string? InterfaceType { get; set; }

    [Description("Network interface operating mode. Optional for networkInterface kind.")]
    public string? InterfaceOperatingMode { get; set; }

    [Description("Exact nodeId reported by read_hardware_config. Required for node kind and configure_network_device.")]
    public string? NodeId { get; set; }

    [Description("Exact subnetId reported by read_hardware_config. Required for subnet and ioSystem kinds.")]
    public string? SubnetId { get; set; }

    [Description("IO system number within its subnet. Required for ioSystem kind.")]
    public int? Number { get; set; }

    [Description("Connection index within the device. Required for communicationConnection kind.")]
    public int? ConnectionIndex { get; set; }

    [Description("Connection type. Optional for communicationConnection kind.")]
    public string? ConnectionType { get; set; }

    [Description("Local connection name. Optional for communicationConnection kind.")]
    public string? LocalConnectionName { get; set; }

    [Description("Local connection identifier. Optional for communicationConnection kind.")]
    public string? LocalConnectionId { get; set; }
}

/// <summary>
/// One segment of the path through a device's item hierarchy. Strict — unknown fields
/// are rejected to prevent silent misidentification.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkDeviceItemPathSegment
{
    [Description("Position number of the module at this level of the device hierarchy.")]
    public int? PositionNumber { get; set; }
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
