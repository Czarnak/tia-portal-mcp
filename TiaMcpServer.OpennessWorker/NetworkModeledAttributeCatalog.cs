using System;
using System.Collections.Generic;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker;

public sealed class NetworkModeledAttributeDescriptor
{
    public NetworkModeledAttributeDescriptor(string name, string expectedClrTypeName, string adapterKey)
    {
        Name = name;
        ExpectedClrTypeName = expectedClrTypeName;
        AdapterKey = adapterKey;
    }

    public string Name { get; }
    public string ExpectedClrTypeName { get; }
    public string AdapterKey { get; }
}

/// <summary>
/// Siemens-free declaration of the typed attributes exposed by the five core network kinds.
/// The lists are ordinally sorted so inspection output is deterministic before dynamic metadata
/// is merged into it.
/// </summary>
public static class NetworkModeledAttributeCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<NetworkModeledAttributeDescriptor>> ByKind =
        new Dictionary<string, IReadOnlyList<NetworkModeledAttributeDescriptor>>(StringComparer.Ordinal)
        {
            [NetworkObjectKinds.DeviceItem] = new[]
            {
                Descriptor("Classification", "Siemens.Engineering.HW.DeviceItemClassifications", "deviceItem.Classification"),
                Descriptor("IsBuiltIn", "System.Boolean", "deviceItem.IsBuiltIn"),
                Descriptor("IsPlugged", "System.Boolean", "deviceItem.IsPlugged"),
                Descriptor("Name", "System.String", "deviceItem.Name"),
                Descriptor("PositionNumber", "System.Int32", "deviceItem.PositionNumber"),
                Descriptor("TypeIdentifier", "System.String", "deviceItem.TypeIdentifier"),
            },
            [NetworkObjectKinds.NetworkInterface] = new[]
            {
                Descriptor("InterfaceOperatingMode", "Siemens.Engineering.HW.InterfaceOperatingModes", "networkInterface.InterfaceOperatingMode"),
                Descriptor("InterfaceType", "Siemens.Engineering.HW.NetType", "networkInterface.InterfaceType"),
            },
            [NetworkObjectKinds.Node] = new[]
            {
                Descriptor("Name", "System.String", "node.Name"),
                Descriptor("NodeId", "System.String", "node.NodeId"),
                Descriptor("NodeType", "Siemens.Engineering.HW.NetType", "node.NodeType"),
            },
            [NetworkObjectKinds.Subnet] = new[]
            {
                Descriptor("Name", "System.String", "subnet.Name"),
                Descriptor("NetworkType", "Siemens.Engineering.HW.NetType", "subnet.NetworkType"),
                Descriptor("TypeIdentifier", "System.String", "subnet.TypeIdentifier"),
            },
            [NetworkObjectKinds.IoSystem] = new[]
            {
                Descriptor("Name", "System.String", "ioSystem.Name"),
                Descriptor("Number", "System.Int32", "ioSystem.Number"),
            },
        };

    public static IReadOnlyList<NetworkModeledAttributeDescriptor> ForKind(string kind)
        => ByKind.TryGetValue(kind, out var descriptors)
            ? descriptors
            : Array.Empty<NetworkModeledAttributeDescriptor>();

    private static NetworkModeledAttributeDescriptor Descriptor(
        string name,
        string expectedClrTypeName,
        string adapterKey)
        => new(name, expectedClrTypeName, adapterKey);
}
