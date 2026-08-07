using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Network;

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

        Assert.NotNull(item.CommunicationConnections);
        Assert.NotNull(item.NetworkInterfaces);
        Assert.NotNull(item.Items);
        Assert.Empty(item.CommunicationConnections);
        Assert.Empty(item.NetworkInterfaces);
        Assert.Empty(item.Items);
        Assert.NotNull(new DeviceInfo().Items);
        Assert.NotNull(new NetworkInterfaceInfo().Nodes);
        Assert.NotNull(new SubnetInfo().IoSystems);
        Assert.NotNull(new SubnetInfo().ConnectedNodeNames);
        Assert.NotNull(new IoSystemInfo().ConnectedDeviceNames);

        var json = JsonSerializer.Serialize(item, JsonOptions);
        Assert.Contains("\"communicationConnections\":[]", json);
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

    // -------------------------------------------------------------------------
    // Selector metadata on hardware DTOs (Task 3)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Each selector-aware DTO initialises <see cref="DeviceItemInfo.SelectorDiagnostics"/> and
    /// friends to non-null empty lists so a walker never has to distinguish "no diagnostics" from
    /// "collection was omitted".
    /// </summary>
    [Fact]
    public void SelectorDiagnostics_DefaultsToEmptyListOnAllDtos()
    {
        Assert.NotNull(new DeviceItemInfo().SelectorDiagnostics);
        Assert.Empty(new DeviceItemInfo().SelectorDiagnostics);
        Assert.NotNull(new NetworkInterfaceInfo().SelectorDiagnostics);
        Assert.Empty(new NetworkInterfaceInfo().SelectorDiagnostics);
        Assert.NotNull(new NodeInfo().SelectorDiagnostics);
        Assert.Empty(new NodeInfo().SelectorDiagnostics);
        Assert.NotNull(new SubnetInfo().SelectorDiagnostics);
        Assert.Empty(new SubnetInfo().SelectorDiagnostics);
        Assert.NotNull(new IoSystemInfo().SelectorDiagnostics);
        Assert.Empty(new IoSystemInfo().SelectorDiagnostics);
        Assert.NotNull(new CommunicationConnectionInfo().SelectorDiagnostics);
        Assert.Empty(new CommunicationConnectionInfo().SelectorDiagnostics);
    }

    /// <summary>
    /// Selectable defaults to false and Selector to null: an unpopulated DTO should never
    /// appear selectable.
    /// </summary>
    [Fact]
    public void Selectable_DefaultsFalse_SelectorDefaultsNull()
    {
        var item = new DeviceItemInfo();

        Assert.False(item.Selectable);
        Assert.Null(item.Selector);
    }

    /// <summary>
    /// The selector metadata survives a JSON round-trip so callers reading from the network layer
    /// can copy a selector out of the hardware config payload and forward it to an inspect call.
    /// </summary>
    [Fact]
    public void SelectorMetadata_RoundTripsViaJson()
    {
        var selector = new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.DeviceItem,
            DeviceName = "PLC_1",
            ItemPath = new List<DeviceItemPathSegmentInfo>
            {
                new() { Index = 0, Name = "Rack_0", PositionNumber = 0, TypeIdentifier = "Rack" },
            }
        };

        var item = new DeviceItemInfo
        {
            Name = "Rack_0",
            Selectable = true,
            Selector = selector,
            SelectorDiagnostics = new List<string>(),
        };

        var json = JsonSerializer.Serialize(item, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<DeviceItemInfo>(json, JsonOptions)!;

        Assert.True(roundTripped.Selectable);
        Assert.NotNull(roundTripped.Selector);
        Assert.Equal(NetworkObjectKinds.DeviceItem, roundTripped.Selector!.Kind);
        Assert.Equal("PLC_1", roundTripped.Selector.DeviceName);
        Assert.NotNull(roundTripped.Selector.ItemPath);
        var seg = Assert.Single(roundTripped.Selector.ItemPath!);
        Assert.Equal(0, seg.Index);
        Assert.Equal("Rack_0", seg.Name);
        Assert.Equal(0, seg.PositionNumber);
        Assert.Equal("Rack", seg.TypeIdentifier);
    }

    [Fact]
    public void UnselectableItem_SerializesDiagnosticMessage()
    {
        var item = new DeviceItemInfo
        {
            Name = null,
            Selectable = false,
            Selector = null,
            SelectorDiagnostics = { "Device name could not be read; selector not available." },
        };

        var json = JsonSerializer.Serialize(item, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<DeviceItemInfo>(json, JsonOptions)!;

        Assert.False(roundTripped.Selectable);
        Assert.Null(roundTripped.Selector);
        Assert.Equal(
            "Device name could not be read; selector not available.",
            Assert.Single(roundTripped.SelectorDiagnostics));
    }

    /// <summary>
    /// DeviceItemPathSegmentInfo carries all four evidence fields: zero-based sibling index,
    /// name, position number, and type identifier. Each must round-trip.
    /// </summary>
    [Fact]
    public void DeviceItemPathSegmentInfo_RoundTripsAllFourFields()
    {
        var seg = new DeviceItemPathSegmentInfo
        {
            Index = 2,
            Name = "CPU_Slot",
            PositionNumber = 3,
            TypeIdentifier = "OrderNumber:CPU",
        };

        var json = JsonSerializer.Serialize(seg, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<DeviceItemPathSegmentInfo>(json, JsonOptions)!;

        Assert.Equal(2, roundTripped.Index);
        Assert.Equal("CPU_Slot", roundTripped.Name);
        Assert.Equal(3, roundTripped.PositionNumber);
        Assert.Equal("OrderNumber:CPU", roundTripped.TypeIdentifier);
    }

    /// <summary>
    /// Subnets and IO systems also carry selector metadata.
    /// </summary>
    [Fact]
    public void SubnetAndIoSystem_SelectableAndSelectorSurviveRoundTrip()
    {
        var config = new HardwareConfigInfo
        {
            Subnets =
            {
                new SubnetInfo
                {
                    Name = "PN/IE_1",
                    SubnetId = "subnet-1",
                    Selectable = true,
                    Selector = new NetworkObjectSelectorInfo
                    {
                        Kind = NetworkObjectKinds.Subnet,
                        SubnetId = "subnet-1",
                    },
                    IoSystems =
                    {
                        new IoSystemInfo
                        {
                            Name = "IO system_1",
                            Number = 100,
                            Selectable = true,
                            Selector = new NetworkObjectSelectorInfo
                            {
                                Kind = NetworkObjectKinds.IoSystem,
                                SubnetId = "subnet-1",
                                Number = 100,
                            }
                        }
                    }
                }
            }
        };

        var roundTripped = RoundTrip(config);
        var subnet = Assert.Single(roundTripped.Subnets);
        var ioSystem = Assert.Single(subnet.IoSystems);

        Assert.True(subnet.Selectable);
        Assert.Equal(NetworkObjectKinds.Subnet, subnet.Selector!.Kind);
        Assert.True(ioSystem.Selectable);
        Assert.Equal(NetworkObjectKinds.IoSystem, ioSystem.Selector!.Kind);
        Assert.Equal(100, ioSystem.Selector.Number);
    }

    [Fact]
    public void CommunicationConnectionSummaries_RoundTripUnderOwningDeviceItem()
    {
        var item = new DeviceItemInfo
        {
            Name = "CPU_1",
            CommunicationConnections =
            {
                new CommunicationConnectionInfo
                {
                    ConnectionType = "S7Connection",
                    LocalConnectionName = "S7_Connection_1",
                    LocalConnectionId = "16#1001",
                    PartnerName = "PLC_2",
                    IsValid = true,
                    Selectable = true,
                    Selector = new NetworkObjectSelectorInfo
                    {
                        Kind = NetworkObjectKinds.CommunicationConnection,
                        DeviceName = "PLC_1",
                        ItemPath = new List<DeviceItemPathSegmentInfo>
                        {
                            new()
                            {
                                Index = 0,
                                Name = "CPU_1",
                                PositionNumber = 1,
                                TypeIdentifier = "OrderNumber:CPU",
                            },
                        },
                        ConnectionIndex = 0,
                        ConnectionType = "S7Connection",
                        LocalConnectionName = "S7_Connection_1",
                        LocalConnectionId = "16#1001",
                    },
                },
                new CommunicationConnectionInfo
                {
                    ConnectionType = "HmiConnection",
                    LocalConnectionName = "HMI_Connection_1",
                    LocalConnectionId = null,
                    PartnerName = "PLC_1",
                    IsValid = true,
                    Selectable = true,
                    Selector = new NetworkObjectSelectorInfo
                    {
                        Kind = NetworkObjectKinds.CommunicationConnection,
                        DeviceName = "HMI_1",
                        ItemPath = new List<DeviceItemPathSegmentInfo>
                        {
                            new()
                            {
                                Index = 0,
                                Name = "HMI_1",
                                PositionNumber = 0,
                                TypeIdentifier = "OrderNumber:HMI",
                            },
                        },
                        ConnectionIndex = 1,
                        ConnectionType = "HmiConnection",
                        LocalConnectionName = "HMI_Connection_1",
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(item, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<DeviceItemInfo>(json, JsonOptions)!;

        Assert.Equal(2, roundTripped.CommunicationConnections.Count);
        var s7 = roundTripped.CommunicationConnections[0];
        var hmi = roundTripped.CommunicationConnections[1];
        Assert.Equal("16#1001", s7.LocalConnectionId);
        Assert.Equal(0, s7.Selector!.ConnectionIndex);
        Assert.Equal("PLC_2", s7.PartnerName);
        Assert.Null(hmi.LocalConnectionId);
        Assert.Null(hmi.Selector!.LocalConnectionId);
        Assert.Equal(1, hmi.Selector.ConnectionIndex);
    }

    private static HardwareConfigInfo RoundTrip(HardwareConfigInfo config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        return JsonSerializer.Deserialize<HardwareConfigInfo>(json, JsonOptions)!;
    }
}
