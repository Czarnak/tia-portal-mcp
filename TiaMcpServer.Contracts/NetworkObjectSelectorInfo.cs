using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// One segment of a path through a device's item hierarchy.
/// Used in <see cref="NetworkObjectSelectorInfo.ItemPath"/> when targeting a device item.
/// Carries four pieces of evidence so a resolver can locate the item by position, name, or type.
/// </summary>
public sealed class DeviceItemPathSegmentInfo
{
    /// <summary>Zero-based sibling index within the parent device item composition.</summary>
    public int Index { get; set; }

    /// <summary>Name of the module at this level of the device hierarchy.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Position number of the module at this level of the device hierarchy.</summary>
    public int PositionNumber { get; set; }

    /// <summary>Type identifier of the module at this level of the device hierarchy.</summary>
    public string TypeIdentifier { get; set; } = string.Empty;
}

/// <summary>
/// Identifies exactly one existing network object. The set of non-null fields determines the kind
/// and must satisfy the per-kind shape enforced by the host catalog.
///
/// <para>
/// This type is the output/contract representation. It is serialization-compatible with the host's
/// strict <c>NetworkObjectTarget</c> request type so selectors embedded in read results can be
/// forwarded directly into an inspect request without transformation.
/// </para>
/// </summary>
public sealed class NetworkObjectSelectorInfo
{
    /// <summary>One of the values declared in <see cref="NetworkObjectKinds"/>.</summary>
    public string? Kind { get; set; }

    /// <summary>Exact device name. Required for deviceItem, networkInterface, node, and communicationConnection kinds.</summary>
    public string? DeviceName { get; set; }

    /// <summary>Path through the device item hierarchy. Required for deviceItem, networkInterface, and communicationConnection kinds.</summary>
    public List<DeviceItemPathSegmentInfo>? ItemPath { get; set; }

    /// <summary>Optional captured interface-name evidence for networkInterface kind.</summary>
    public string? InterfaceName { get; set; }

    /// <summary>Interface type (e.g., PROFINET, PROFIBUS). Optional for networkInterface kind.</summary>
    public string? InterfaceType { get; set; }

    /// <summary>Interface operating mode. Optional for networkInterface kind.</summary>
    public string? InterfaceOperatingMode { get; set; }

    /// <summary>Exact node identifier reported by read_hardware_config. Required for node kind.</summary>
    public string? NodeId { get; set; }

    /// <summary>Exact subnet identifier reported by read_hardware_config. Required for subnet and ioSystem kinds.</summary>
    public string? SubnetId { get; set; }

    /// <summary>IO system number within its subnet. Required for ioSystem kind.</summary>
    public int? Number { get; set; }

    /// <summary>Connection index within the device. Required for communicationConnection kind.</summary>
    public int? ConnectionIndex { get; set; }

    /// <summary>Connection type. Required for communicationConnection kind.</summary>
    public string? ConnectionType { get; set; }

    /// <summary>Local connection name. Required for communicationConnection kind.</summary>
    public string? LocalConnectionName { get; set; }

    /// <summary>Local connection identifier. Optional for communicationConnection kind.</summary>
    public string? LocalConnectionId { get; set; }
}
