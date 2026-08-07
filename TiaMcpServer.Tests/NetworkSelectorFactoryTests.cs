using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Unit tests for <see cref="NetworkSelectorFactory"/>.
/// The factory is Siemens-free so all cases are exercisable here.
/// </summary>
public class NetworkSelectorFactoryTests
{
    // -------------------------------------------------------------------------
    // DeviceItem
    // -------------------------------------------------------------------------

    [Fact]
    public void DeviceItem_CreatesCorrectSelector()
    {
        var path = new List<DeviceItemPathSegmentInfo>
        {
            new() { Index = 0, Name = "Rack_0", PositionNumber = 0, TypeIdentifier = "Rack" },
            new() { Index = 1, Name = "CPU_Slot", PositionNumber = 1, TypeIdentifier = "CPU" },
        };

        var selector = NetworkSelectorFactory.DeviceItem("PLC_1", path);

        Assert.Equal(NetworkObjectKinds.DeviceItem, selector.Kind);
        Assert.Equal("PLC_1", selector.DeviceName);
        Assert.NotNull(selector.ItemPath);
        Assert.Equal(2, selector.ItemPath!.Count);
        Assert.Equal(0, selector.ItemPath[0].Index);
        Assert.Equal("Rack_0", selector.ItemPath[0].Name);
        Assert.Equal(0, selector.ItemPath[0].PositionNumber);
        Assert.Equal("Rack", selector.ItemPath[0].TypeIdentifier);
        Assert.Equal(1, selector.ItemPath[1].Index);
        Assert.Equal("CPU_Slot", selector.ItemPath[1].Name);
    }

    [Fact]
    public void DeviceItem_ClonesItemPath_MutationDoesNotAffectSelector()
    {
        var path = new List<DeviceItemPathSegmentInfo>
        {
            ValidSegment(0, "Rack_0"),
        };

        var selector = NetworkSelectorFactory.DeviceItem("PLC_1", path);
        path.Add(new DeviceItemPathSegmentInfo { Index = 99, Name = "injected" });

        // The selector's list should not grow.
        Assert.Single(selector.ItemPath!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\t")]
    public void DeviceItem_RejectsBlankDeviceName(string deviceName)
    {
        var path = new List<DeviceItemPathSegmentInfo> { ValidSegment(0, "Rack_0") };

        Assert.Throws<ArgumentException>(() => NetworkSelectorFactory.DeviceItem(deviceName, path));
    }

    [Fact]
    public void DeviceItem_RejectsEmptyItemPath()
    {
        Assert.Throws<ArgumentException>(
            () => NetworkSelectorFactory.DeviceItem("PLC_1", new List<DeviceItemPathSegmentInfo>()));
    }

    [Fact]
    public void DeviceItem_RejectsNegativeIndexInSegment()
    {
        var path = new List<DeviceItemPathSegmentInfo>
        {
            ValidSegment(-1, "Bad"),
        };

        Assert.Throws<ArgumentException>(() => NetworkSelectorFactory.DeviceItem("PLC_1", path));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("positionNumber")]
    [InlineData("typeIdentifier")]
    public void DeviceItem_RejectsIncompleteOrNegativePathEvidence(string field)
    {
        var segment = new DeviceItemPathSegmentInfo
        {
            Index = 0,
            Name = "CPU",
            PositionNumber = 1,
            TypeIdentifier = "OrderNumber:CPU",
        };
        switch (field)
        {
            case "name": segment.Name = "   "; break;
            case "positionNumber": segment.PositionNumber = -1; break;
            case "typeIdentifier": segment.TypeIdentifier = "   "; break;
        }

        Assert.Throws<ArgumentException>(
            () => NetworkSelectorFactory.DeviceItem("PLC_1", new[] { segment }));
    }

    [Fact]
    public void DeviceItem_RejectsNullPathSegment()
    {
        Assert.Throws<ArgumentException>(
            () => NetworkSelectorFactory.DeviceItem(
                "PLC_1",
                new DeviceItemPathSegmentInfo[] { null! }));
    }

    // -------------------------------------------------------------------------
    // NetworkInterface
    // -------------------------------------------------------------------------

    [Fact]
    public void NetworkInterface_CreatesCorrectSelector()
    {
        var path = new List<DeviceItemPathSegmentInfo>
        {
            new() { Index = 0, Name = "Interface_Slot", PositionNumber = 1, TypeIdentifier = "IF" },
        };

        var selector = NetworkSelectorFactory.NetworkInterface(
            "PLC_1", path, "PROFINET interface_1", "PROFINET", "IoController");

        Assert.Equal(NetworkObjectKinds.NetworkInterface, selector.Kind);
        Assert.Equal("PLC_1", selector.DeviceName);
        Assert.Equal("PROFINET interface_1", selector.InterfaceName);
        Assert.Equal("PROFINET", selector.InterfaceType);
        Assert.Equal("IoController", selector.InterfaceOperatingMode);
        Assert.NotNull(selector.ItemPath);
        Assert.Single(selector.ItemPath!);
    }

    [Fact]
    public void NetworkInterface_AllowsNullOptionals()
    {
        var path = new List<DeviceItemPathSegmentInfo> { ValidSegment(0, "Interface_Slot") };

        var selector = NetworkSelectorFactory.NetworkInterface(
            "PLC_1", path, null, null, null);

        Assert.Equal(NetworkObjectKinds.NetworkInterface, selector.Kind);
        Assert.Null(selector.InterfaceName);
        Assert.Null(selector.InterfaceType);
        Assert.Null(selector.InterfaceOperatingMode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void NetworkInterface_RejectsBlankDeviceName(string deviceName)
    {
        var path = new List<DeviceItemPathSegmentInfo> { ValidSegment(0, "Interface_Slot") };

        Assert.Throws<ArgumentException>(
            () => NetworkSelectorFactory.NetworkInterface(deviceName, path, null, null, null));
    }

    [Fact]
    public void NetworkInterface_RejectsEmptyItemPath()
    {
        Assert.Throws<ArgumentException>(
            () => NetworkSelectorFactory.NetworkInterface(
                "PLC_1", new List<DeviceItemPathSegmentInfo>(), null, null, null));
    }

    // -------------------------------------------------------------------------
    // Node
    // -------------------------------------------------------------------------

    [Fact]
    public void Node_CreatesCorrectSelector()
    {
        var path = new[] { ValidSegment(0, "Interface_1") };
        var selector = NetworkSelectorFactory.Node("PLC_1", "node-42", path, 1);

        Assert.Equal(NetworkObjectKinds.Node, selector.Kind);
        Assert.Equal("PLC_1", selector.DeviceName);
        Assert.Equal("node-42", selector.NodeId);
        Assert.Equal(1, selector.NodeIndex);
        Assert.Equal("Interface_1", Assert.Single(selector.ItemPath!).Name);
    }

    [Theory]
    [InlineData("", "node-1")]
    [InlineData("  ", "node-1")]
    public void Node_RejectsBlankDeviceName(string deviceName, string nodeId)
    {
        Assert.Throws<ArgumentException>(() => NetworkSelectorFactory.Node(deviceName, nodeId));
    }

    [Theory]
    [InlineData("PLC_1", "")]
    [InlineData("PLC_1", "  ")]
    public void Node_RejectsBlankNodeId(string deviceName, string nodeId)
    {
        Assert.Throws<ArgumentException>(() => NetworkSelectorFactory.Node(deviceName, nodeId));
    }

    // -------------------------------------------------------------------------
    // Subnet
    // -------------------------------------------------------------------------

    [Fact]
    public void Subnet_CreatesCorrectSelector()
    {
        var selector = NetworkSelectorFactory.Subnet("subnet-7");

        Assert.Equal(NetworkObjectKinds.Subnet, selector.Kind);
        Assert.Equal("subnet-7", selector.SubnetId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Subnet_RejectsBlankSubnetId(string subnetId)
    {
        Assert.Throws<ArgumentException>(() => NetworkSelectorFactory.Subnet(subnetId));
    }

    // -------------------------------------------------------------------------
    // IoSystem
    // -------------------------------------------------------------------------

    [Fact]
    public void IoSystem_CreatesCorrectSelector()
    {
        var selector = NetworkSelectorFactory.IoSystem("subnet-3", 100, 1, "IO system_1");

        Assert.Equal(NetworkObjectKinds.IoSystem, selector.Kind);
        Assert.Equal("subnet-3", selector.SubnetId);
        Assert.Equal(100, selector.Number);
        Assert.Equal(1, selector.IoSystemIndex);
        Assert.Equal("IO system_1", selector.IoSystemName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void IoSystem_RejectsBlankSubnetId(string subnetId)
    {
        Assert.Throws<ArgumentException>(() => NetworkSelectorFactory.IoSystem(subnetId, 1));
    }

    [Fact]
    public void IoSystem_RejectsNegativeNumber()
    {
        Assert.Throws<ArgumentException>(() => NetworkSelectorFactory.IoSystem("subnet-1", -1));
    }

    [Fact]
    public void IoSystem_AllowsZeroNumber()
    {
        // Number 0 is a valid IO system number (first system can be 0-based in some contexts).
        var selector = NetworkSelectorFactory.IoSystem("subnet-1", 0);
        Assert.Equal(0, selector.Number);
    }

    // -------------------------------------------------------------------------
    // Duplicate-name device items: index distinguishes siblings
    // -------------------------------------------------------------------------

    [Fact]
    public void DeviceItem_DuplicateNamed_SiblingsHaveDifferentIndices()
    {
        var seg0 = ValidSegment(0, "Slot");
        var seg1 = ValidSegment(1, "Slot");

        var selector0 = NetworkSelectorFactory.DeviceItem("PLC_1", new[] { seg0 });
        var selector1 = NetworkSelectorFactory.DeviceItem("PLC_1", new[] { seg1 });

        Assert.Equal(0, selector0.ItemPath![0].Index);
        Assert.Equal(1, selector1.ItemPath![0].Index);
    }

    private static DeviceItemPathSegmentInfo ValidSegment(int index, string name) => new()
    {
        Index = index,
        Name = name,
        PositionNumber = index < 0 ? 0 : index,
        TypeIdentifier = "OrderNumber:Fixture",
    };
}
