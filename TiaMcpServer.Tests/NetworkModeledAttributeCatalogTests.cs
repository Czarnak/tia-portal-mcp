using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests;

public class NetworkModeledAttributeCatalogTests
{
    public static TheoryData<string, (string Name, string ExpectedClrTypeName, string AdapterKey)[]> CoreKinds() => new()
    {
        {
            NetworkObjectKinds.DeviceItem,
            new[]
            {
                ("Classification", "Siemens.Engineering.HW.DeviceItemClassifications", "deviceItem.Classification"),
                ("IsBuiltIn", "System.Boolean", "deviceItem.IsBuiltIn"),
                ("IsPlugged", "System.Boolean", "deviceItem.IsPlugged"),
                ("Name", "System.String", "deviceItem.Name"),
                ("PositionNumber", "System.Int32", "deviceItem.PositionNumber"),
                ("TypeIdentifier", "System.String", "deviceItem.TypeIdentifier"),
            }
        },
        {
            NetworkObjectKinds.NetworkInterface,
            new[]
            {
                ("InterfaceOperatingMode", "Siemens.Engineering.HW.InterfaceOperatingModes", "networkInterface.InterfaceOperatingMode"),
                ("InterfaceType", "Siemens.Engineering.HW.NetType", "networkInterface.InterfaceType"),
            }
        },
        {
            NetworkObjectKinds.Node,
            new[]
            {
                ("Name", "System.String", "node.Name"),
                ("NodeId", "System.String", "node.NodeId"),
                ("NodeType", "Siemens.Engineering.HW.NetType", "node.NodeType"),
            }
        },
        {
            NetworkObjectKinds.Subnet,
            new[]
            {
                ("Name", "System.String", "subnet.Name"),
                ("NetworkType", "Siemens.Engineering.HW.NetType", "subnet.NetworkType"),
                ("TypeIdentifier", "System.String", "subnet.TypeIdentifier"),
            }
        },
        {
            NetworkObjectKinds.IoSystem,
            new[]
            {
                ("Name", "System.String", "ioSystem.Name"),
                ("Number", "System.Int32", "ioSystem.Number"),
            }
        },
    };

    [Theory]
    [MemberData(nameof(CoreKinds))]
    public void ForKind_ReturnsExactOrderedDescriptors(
        string kind,
        (string Name, string ExpectedClrTypeName, string AdapterKey)[] expected)
    {
        var actual = NetworkModeledAttributeCatalog.ForKind(kind);

        Assert.Equal(expected, actual.Select(descriptor =>
            (descriptor.Name, descriptor.ExpectedClrTypeName, descriptor.AdapterKey)));
    }

    [Theory]
    [MemberData(nameof(CoreKinds))]
    public void ForKind_HasUniqueOrdinalNamesAndOneReaderContract(
        string kind,
        (string Name, string ExpectedClrTypeName, string AdapterKey)[] _)
    {
        var descriptors = NetworkModeledAttributeCatalog.ForKind(kind);

        Assert.Equal(
            descriptors.Select(descriptor => descriptor.Name).OrderBy(name => name, StringComparer.Ordinal),
            descriptors.Select(descriptor => descriptor.Name));
        Assert.Equal(
            descriptors.Count,
            descriptors.Select(descriptor => descriptor.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(descriptors, descriptor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.ExpectedClrTypeName));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.AdapterKey));
        });
    }
}
