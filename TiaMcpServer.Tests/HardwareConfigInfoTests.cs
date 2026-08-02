using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests;

public class HardwareConfigInfoTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void SerializesEmptyConfig()
    {
        var config = new HardwareConfigInfo();

        var roundTripped = RoundTrip(config);

        Assert.NotNull(roundTripped.Devices);
        Assert.NotNull(roundTripped.Subnets);
        Assert.Empty(roundTripped.Devices);
        Assert.Empty(roundTripped.Subnets);
    }

    [Fact]
    public void RoundTripsFullDeviceTree()
    {
        var config = new HardwareConfigInfo
        {
            Devices =
            {
                new DeviceInfo
                {
                    Name = "PLC_1",
                    TypeIdentifier = "OrderNumber:6ES7",
                    Items =
                    {
                        new DeviceItemInfo
                        {
                            Name = "Rack_0",
                            TypeIdentifier = "Rack",
                            PositionNumber = 0,
                            Address = "0..1",
                            NetworkInterfaces = new List<NetworkInterfaceInfo>
                            {
                                new NetworkInterfaceInfo
                                {
                                    Name = "PN/IE_1",
                                    Nodes =
                                    {
                                        new NodeInfo
                                        {
                                            Name = "X1",
                                            IpAddress = "192.168.0.10",
                                            SubnetMask = "255.255.255.0",
                                            PnDeviceName = "plc-1",
                                            SubnetName = "PN/IE_1",
                                            IoSystemName = "IO system_1"
                                        }
                                    }
                                }
                            },
                            Items = new List<DeviceItemInfo>
                            {
                                new DeviceItemInfo
                                {
                                    Name = "DI_16",
                                    TypeIdentifier = "InputModule",
                                    PositionNumber = 1,
                                    Address = "0..1"
                                }
                            }
                        }
                    }
                }
            }
        };

        var roundTripped = RoundTrip(config);
        var device = Assert.Single(roundTripped.Devices);
        var item = Assert.Single(device.Items);
        Assert.NotNull(item.NetworkInterfaces);
        Assert.NotNull(item.Items);
        var networkInterface = Assert.Single(item.NetworkInterfaces);
        var node = Assert.Single(networkInterface.Nodes);
        var child = Assert.Single(item.Items);

        Assert.Equal("PLC_1", device.Name);
        Assert.Equal("OrderNumber:6ES7", device.TypeIdentifier);
        Assert.Equal("Rack_0", item.Name);
        Assert.Equal("Rack", item.TypeIdentifier);
        Assert.Equal(0, item.PositionNumber);
        Assert.Equal("0..1", item.Address);
        Assert.Equal("PN/IE_1", networkInterface.Name);
        Assert.Equal("X1", node.Name);
        Assert.Equal("192.168.0.10", node.IpAddress);
        Assert.Equal("255.255.255.0", node.SubnetMask);
        Assert.Equal("plc-1", node.PnDeviceName);
        Assert.Equal("PN/IE_1", node.SubnetName);
        Assert.Equal("IO system_1", node.IoSystemName);
        Assert.Equal("DI_16", child.Name);
    }

    [Fact]
    public void RoundTripsSubnetWithIoSystem()
    {
        var config = new HardwareConfigInfo
        {
            Subnets =
            {
                new SubnetInfo
                {
                    Name = "PN/IE_1",
                    TypeIdentifier = "Ethernet",
                    ConnectedNodeNames = { "PLC_1.X1" },
                    IoSystems =
                    {
                        new IoSystemInfo
                        {
                            Name = "IO system_1",
                            IoControllerName = "PLC_1",
                            ConnectedDeviceNames = { "ET200SP_1" }
                        }
                    }
                }
            }
        };

        var roundTripped = RoundTrip(config);
        var subnet = Assert.Single(roundTripped.Subnets);
        var ioSystem = Assert.Single(subnet.IoSystems);

        Assert.Equal("PN/IE_1", subnet.Name);
        Assert.Equal("Ethernet", subnet.TypeIdentifier);
        Assert.Equal(new[] { "PLC_1.X1" }, subnet.ConnectedNodeNames);
        Assert.Equal("IO system_1", ioSystem.Name);
        Assert.Equal("PLC_1", ioSystem.IoControllerName);
        Assert.Equal(new[] { "ET200SP_1" }, ioSystem.ConnectedDeviceNames);
    }

    [Fact]
    public void NullableFieldsSerializeAsNull()
    {
        var item = new DeviceItemInfo
        {
            Name = "Rack_0",
            TypeIdentifier = "Rack",
            Address = null
        };

        var json = JsonSerializer.Serialize(item, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<DeviceItemInfo>(json, JsonOptions);

        Assert.DoesNotContain("address", json);
        Assert.NotNull(roundTripped);
        Assert.Null(roundTripped.Address);
    }

    /// <summary>
    /// Task 6 resolves write selectors by walking this tree. A collection that can be null forces
    /// every walker to null-guard and lets an unresolved branch look like an absent one, so every
    /// collection in the hardware DTO tree is non-null by default and always serialized.
    /// </summary>
    [Fact]
    public void EveryCollectionInTheHardwareTreeIsNonNullByDefault()
    {
        var item = new DeviceItemInfo();

        Assert.NotNull(item.NetworkInterfaces);
        Assert.NotNull(item.Items);
        Assert.Empty(item.NetworkInterfaces);
        Assert.Empty(item.Items);
        Assert.NotNull(new DeviceInfo().Items);
        Assert.NotNull(new NetworkInterfaceInfo().Nodes);
        Assert.NotNull(new SubnetInfo().IoSystems);
        Assert.NotNull(new SubnetInfo().ConnectedNodeNames);
        Assert.NotNull(new IoSystemInfo().ConnectedDeviceNames);

        var json = JsonSerializer.Serialize(item, JsonOptions);
        Assert.Contains("\"networkInterfaces\":[]", json);
        Assert.Contains("\"items\":[]", json);
    }

    /// <summary>
    /// An identity that could not be read stays empty or null. It is never defaulted to a value a
    /// write selector could match, because that would let an unreadable target satisfy a selector.
    /// </summary>
    [Fact]
    public void UnreadIdentitiesAreEmptyOrNullNeverInvented()
    {
        Assert.Equal(string.Empty, new NodeInfo().NodeId);
        Assert.Null(new NodeInfo().NodeType);
        Assert.Equal(string.Empty, new SubnetInfo().SubnetId);
        Assert.Null(new SubnetInfo().NetworkType);
        Assert.Null(new IoSystemInfo().Number);
    }

    [Fact]
    public void IdentityMembersUseTheirDeclaredJsonNames()
    {
        var config = new HardwareConfigInfo
        {
            Devices =
            {
                new DeviceInfo
                {
                    Name = "PLC_1",
                    Items =
                    {
                        new DeviceItemInfo
                        {
                            Name = "PROFINET interface_1",
                            NetworkInterfaces =
                            {
                                new NetworkInterfaceInfo
                                {
                                    Name = "PROFINET interface_1",
                                    Nodes =
                                    {
                                        new NodeInfo { Name = "X1", NodeId = "0", NodeType = "Ethernet" }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            Subnets =
            {
                new SubnetInfo
                {
                    Name = "PN/IE_1",
                    SubnetId = "subnet-1",
                    NetworkType = "Ethernet",
                    IoSystems = { new IoSystemInfo { Name = "IO system_1", Number = 100 } }
                }
            }
        };

        var json = JsonSerializer.Serialize(config, JsonOptions);

        Assert.Contains("\"nodeId\":\"0\"", json);
        Assert.Contains("\"nodeType\":\"Ethernet\"", json);
        Assert.Contains("\"subnetId\":\"subnet-1\"", json);
        Assert.Contains("\"networkType\":\"Ethernet\"", json);
        Assert.Contains("\"number\":100", json);
    }

    /// <summary>
    /// Read fixture for a multi-homed PC station: one station, two interfaces, one node each. The
    /// PLC-facing and client-database-facing nodes carry different node ids, so a selector can
    /// address either one without touching the other.
    /// </summary>
    [Fact]
    public void PcStationWithTwoInterfaces_KeepsBothNodesSeparatelyAddressable()
    {
        var config = new HardwareConfigInfo
        {
            Devices =
            {
                new DeviceInfo
                {
                    Name = "PC_System_1",
                    TypeIdentifier = "OrderNumber:PC-System",
                    Items =
                    {
                        new DeviceItemInfo
                        {
                            Name = "IE general_1",
                            TypeIdentifier = "OrderNumber:IE-General",
                            PositionNumber = 1,
                            NetworkInterfaces =
                            {
                                new NetworkInterfaceInfo
                                {
                                    Name = "PROFINET interface_1",
                                    Nodes =
                                    {
                                        new NodeInfo
                                        {
                                            Name = "E1",
                                            NodeId = "0",
                                            NodeType = "Ethernet",
                                            IpAddress = "192.168.0.20",
                                            SubnetName = "PN/IE_1"
                                        }
                                    }
                                }
                            }
                        },
                        new DeviceItemInfo
                        {
                            Name = "IE general_2",
                            TypeIdentifier = "OrderNumber:IE-General",
                            PositionNumber = 2,
                            NetworkInterfaces =
                            {
                                new NetworkInterfaceInfo
                                {
                                    Name = "PROFINET interface_2",
                                    Nodes =
                                    {
                                        new NodeInfo
                                        {
                                            Name = "E2",
                                            NodeId = "1",
                                            NodeType = "Ethernet",
                                            IpAddress = "10.0.0.20",
                                            SubnetName = "PN/IE_2"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var station = Assert.Single(RoundTrip(config).Devices);
        var nodes = station.Items
            .SelectMany(item => item.NetworkInterfaces)
            .SelectMany(networkInterface => networkInterface.Nodes)
            .ToList();

        Assert.Equal(2, nodes.Count);
        Assert.Equal(new[] { "0", "1" }, nodes.Select(node => node.NodeId));
        Assert.Equal(2, nodes.Select(node => node.NodeId).Distinct().Count());

        var plcFacing = Assert.Single(nodes.Where(node => node.NodeId == "0"));
        var clientDatabaseFacing = Assert.Single(nodes.Where(node => node.NodeId == "1"));

        Assert.Equal("192.168.0.20", plcFacing.IpAddress);
        Assert.Equal("PN/IE_1", plcFacing.SubnetName);
        Assert.Equal("10.0.0.20", clientDatabaseFacing.IpAddress);
        Assert.Equal("PN/IE_2", clientDatabaseFacing.SubnetName);
    }

    [Fact]
    public void MessagesRoundTrip()
    {
        var config = new HardwareConfigInfo
        {
            Messages = { "Could not read device 'X' type identifier: access denied." }
        };

        var json = JsonSerializer.Serialize(config);
        var roundTripped = JsonSerializer.Deserialize<HardwareConfigInfo>(json)!;

        Assert.Equal(
            "Could not read device 'X' type identifier: access denied.",
            Assert.Single(roundTripped.Messages));
    }

    [Fact]
    public void UnreadableValues_AreNullNotFallbackDefaults()
    {
        var item = new DeviceItemInfo();

        Assert.Null(item.Name);
        Assert.Null(item.TypeIdentifier);
        Assert.Null(item.PositionNumber);
    }

    private static HardwareConfigInfo RoundTrip(HardwareConfigInfo config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        return JsonSerializer.Deserialize<HardwareConfigInfo>(json, JsonOptions)!;
    }
}
