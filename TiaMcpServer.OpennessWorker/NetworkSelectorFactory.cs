using System;
using System.Collections.Generic;
using System.Linq;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker;

/// <summary>
/// Pure, Siemens-free factory that builds deterministic <see cref="NetworkObjectSelectorInfo"/>
/// values from the evidence gathered during a hardware-config read.
///
/// <para>
/// Every method clones list arguments so callers can reuse or mutate the originals without
/// affecting selectors that have already been produced.
/// </para>
///
/// <para>
/// Rejection rules: a blank (null/whitespace) required string or incomplete/negative path
/// evidence causes an <see cref="ArgumentException"/>. An empty item-path list is also rejected
/// because an empty path provides no evidence for device-item resolution.
/// </para>
/// </summary>
public static class NetworkSelectorFactory
{
    /// <summary>
    /// Builds a deviceItem selector from the device name and the ordered path through the device
    /// item composition.
    /// </summary>
    /// <param name="deviceName">Exact device name. Must be non-blank.</param>
    /// <param name="itemPath">
    /// Ordered segments from the root of the device item composition to the target item.
    /// Must be non-empty; every segment must contain a non-negative index and position plus
    /// non-blank name and type-identifier evidence.
    /// </param>
    public static NetworkObjectSelectorInfo DeviceItem(
        string deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> itemPath)
    {
        RequireNonBlank(deviceName, nameof(deviceName));
        RequireNonEmptyPath(itemPath);
        ValidateSegments(itemPath);

        return new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.DeviceItem,
            DeviceName = deviceName,
            ItemPath = itemPath.Select(Clone).ToList(),
        };
    }

    /// <summary>
    /// Builds a networkInterface selector. The optional parameters can be null when the evidence
    /// is not available; only the device name and item path are required.
    /// </summary>
    public static NetworkObjectSelectorInfo NetworkInterface(
        string deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> itemPath,
        string? interfaceName,
        string? interfaceType,
        string? interfaceOperatingMode)
    {
        RequireNonBlank(deviceName, nameof(deviceName));
        RequireNonEmptyPath(itemPath);
        ValidateSegments(itemPath);

        return new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.NetworkInterface,
            DeviceName = deviceName,
            ItemPath = itemPath.Select(Clone).ToList(),
            InterfaceName = interfaceName,
            InterfaceType = interfaceType,
            InterfaceOperatingMode = interfaceOperatingMode,
        };
    }

    /// <summary>Builds a node selector from the device name and the node's own identity.</summary>
    /// <param name="deviceName">Exact device name. Must be non-blank.</param>
    /// <param name="nodeId">Node identity as reported by read_hardware_config. Must be non-blank.</param>
    public static NetworkObjectSelectorInfo Node(string deviceName, string nodeId)
    {
        RequireNonBlank(deviceName, nameof(deviceName));
        RequireNonBlank(nodeId, nameof(nodeId));

        return new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.Node,
            DeviceName = deviceName,
            NodeId = nodeId,
        };
    }

    /// <summary>Builds a subnet selector from the subnet's own identity.</summary>
    /// <param name="subnetId">Subnet identity as reported by read_hardware_config. Must be non-blank.</param>
    public static NetworkObjectSelectorInfo Subnet(string subnetId)
    {
        RequireNonBlank(subnetId, nameof(subnetId));

        return new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.Subnet,
            SubnetId = subnetId,
        };
    }

    /// <summary>Builds an IO system selector from the subnet identity and the IO system number.</summary>
    /// <param name="subnetId">Subnet identity that owns this IO system. Must be non-blank.</param>
    /// <param name="number">IO system number within its subnet. Must be ≥ 0.</param>
    public static NetworkObjectSelectorInfo IoSystem(string subnetId, int number)
    {
        RequireNonBlank(subnetId, nameof(subnetId));

        if (number < 0)
        {
            throw new ArgumentException(
                $"IO system number must be non-negative but was {number}.",
                nameof(number));
        }

        return new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.IoSystem,
            SubnetId = subnetId,
            Number = number,
        };
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static DeviceItemPathSegmentInfo Clone(DeviceItemPathSegmentInfo segment) =>
        new()
        {
            Index = segment.Index,
            Name = segment.Name,
            PositionNumber = segment.PositionNumber,
            TypeIdentifier = segment.TypeIdentifier,
        };

    private static void RequireNonBlank(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"'{paramName}' must be a non-blank string but was '{value}'.",
                paramName);
        }
    }

    private static void RequireNonEmptyPath(IReadOnlyList<DeviceItemPathSegmentInfo> itemPath)
    {
        if (itemPath.Count == 0)
        {
            throw new ArgumentException(
                "Item path must contain at least one segment.",
                nameof(itemPath));
        }
    }

    private static void ValidateSegments(IReadOnlyList<DeviceItemPathSegmentInfo> itemPath)
    {
        for (int i = 0; i < itemPath.Count; i++)
        {
            var segment = itemPath[i];
            if (segment is null)
            {
                throw new ArgumentException(
                    $"Segment at position {i} must not be null.",
                    nameof(itemPath));
            }

            if (segment.Index < 0)
            {
                throw new ArgumentException(
                    $"Segment at position {i} has a negative Index ({segment.Index}), "
                        + "which is not a valid zero-based sibling index.",
                    nameof(itemPath));
            }

            if (string.IsNullOrWhiteSpace(segment.Name))
            {
                throw new ArgumentException(
                    $"Segment at position {i} must contain a non-blank Name.",
                    nameof(itemPath));
            }

            if (segment.PositionNumber < 0)
            {
                throw new ArgumentException(
                    $"Segment at position {i} has a negative PositionNumber "
                        + $"({segment.PositionNumber}).",
                    nameof(itemPath));
            }

            if (string.IsNullOrWhiteSpace(segment.TypeIdentifier))
            {
                throw new ArgumentException(
                    $"Segment at position {i} must contain a non-blank TypeIdentifier.",
                    nameof(itemPath));
            }
        }
    }
}
