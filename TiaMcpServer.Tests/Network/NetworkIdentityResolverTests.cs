using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Pure, Siemens-free tests of <see cref="NetworkIdentityResolver"/>: exact match, duplicate,
/// missing, and unreadable identity variants for device, node, subnet, and IO-system resolution.
/// Every fixture is a synthetic <see cref="HardwareConfigInfo"/> — no worker, no TIA Portal.
/// </summary>
public class NetworkIdentityResolverTests
{
    private static NetworkOperationRequest RepairRequest(int position = 32768) => new()
    {
        OperationId = "repair", Operation = "configure_network_device",
        Target = new() { DeviceName = "S7-1500/ET200MP station_1", NodeId = "E1", InterfacePath = new[]
        { new NetworkInterfacePathSegment { Name = "PLC_DP", PositionNumber = 1 },
          new NetworkInterfacePathSegment { Name = position == 32768 ? "PROFINET interface_1" : "PROFINET interface_2", PositionNumber = position } } },
        Changes = new() { IpAddress = "192.168.12.99" }
    };

    [Theory]
    [InlineData(32768)]
    [InlineData(33024)]
    public void ConfigureEachE1_ResolvesOnlyItsOwner(int position)
    {
        var resolution = NetworkIdentityResolver.Resolve(RepairRequest(position),
            NetworkDiscoveryRepairFixture.Metadata(new() { Scope = "project", Complete = true }));
        Assert.True(resolution.Success, resolution.Error);
        Assert.Equal(position == 32768 ? "X1" : "X2", resolution.Evidence!.NodeName);
    }

    [Fact]
    public void BareDuplicateE1_IsAmbiguousBeforeDispatch()
    {
        var request = RepairRequest(); request.Target!.InterfacePath = null;
        var bareResolution = NetworkIdentityResolver.Resolve(request,
            NetworkDiscoveryRepairFixture.Metadata(new() { Scope = "project", Complete = true }));
        Assert.False(bareResolution.Success);
        Assert.Contains("interfacePath", bareResolution.Error);
    }

    [Theory]
    [InlineData("position")]
    [InlineData("type")]
    [InlineData("interface")]
    [InlineData("index")]
    public void WrongQualifiedConstraints_RefuseWithoutRetargeting(string constraint)
    {
        var request = RepairRequest();
        if (constraint == "position") request.Target!.InterfacePath![1].PositionNumber = 33024;
        if (constraint == "type") request.Target!.InterfacePath![1].TypeIdentifier = "Wrong";
        if (constraint == "interface") request.Target!.InterfaceName = "PROFINET interface_2";
        if (constraint == "index") request.Target!.NodeIndex = 1;
        Assert.False(NetworkIdentityResolver.Resolve(request,
            NetworkDiscoveryRepairFixture.Metadata(new() { Scope = "project", Complete = true })).Success);
    }

    [Theory]
    [InlineData("device", null)]
    [InlineData("device", "")]
    [InlineData("device", " ")]
    [InlineData("node", null)]
    [InlineData("node", "")]
    [InlineData("node", " ")]
    [InlineData("subnet", null)]
    [InlineData("subnet", "")]
    [InlineData("subnet", " ")]
    [InlineData("io", null)]
    public void ReadableMatch_WithUnreadableCompetingIdentity_RefusesSelection(string kind, string? identity)
    {
        var state = MultiHomedPcFixture(new() { Subnet("Subnet_A", "S-1", IoSystemFixture("IO", 1)) });
        var operation = ConfigureRequest("op1", "PC_1", "N-PLC");
        if (kind == "device") state.Devices.Add(new() { Name = identity });
        if (kind == "node") state.Devices[0].Items[0].Items[1].NetworkInterfaces[0].Nodes[0].NodeId = identity!;
        if (kind == "subnet")
        {
            state.Subnets.Add(Subnet("Other", identity!));
            operation.Changes = new() { Subnet = new() { SubnetId = "S-1" } };
        }
        if (kind == "io")
        {
            state.Subnets[0].IoSystems.Add(IoSystemFixture("Unreadable", null));
            operation.Changes = new() { IoSystem = new() { SubnetId = "S-1", Number = 1 } };
        }
        var resolution = NetworkIdentityResolver.Resolve(operation, state);
        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, resolution.FailureCategory);
        Assert.Null(resolution.Evidence);
    }

    [Theory]
    [InlineData("delete_subnet")]
    [InlineData("update_subnet")]
    public void ExistingSubnet_WithUnreadableCompetingId_RefusesSelection(string operationName)
    {
        var state = MultiHomedPcFixture(new() { Subnet("known", "S-1"), Subnet("unknown", "") });
        var operation = new NetworkOperationRequest { OperationId = "op1", Operation = operationName,
            Target = new() { Kind = "subnet", SubnetId = "S-1" }, SubnetChanges = operationName == "update_subnet" ? new() { Name = "changed" } : null };
        var resolution = NetworkIdentityResolver.Resolve(operation, state);
        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, resolution.FailureCategory);
    }

    [Theory]
    [InlineData("device", null)]
    [InlineData("device", "")]
    [InlineData("device", " ")]
    [InlineData("subnet", null)]
    [InlineData("subnet", "")]
    [InlineData("subnet", " ")]
    public void Creation_WithUnreadableCompetingName_RefusesUniqueness(string kind, string? name)
    {
        var state = MultiHomedPcFixture(new() { Subnet("known", "S-1") });
        var operation = kind == "device" ? CreationRequest("op1", "new")
            : new NetworkOperationRequest { OperationId = "op1", Operation = "create_subnet", Subnet = new() { Name = "new", NetworkType = "Ethernet" } };
        if (kind == "device") state.Devices.Add(new() { Name = name });
        else state.Subnets[0].Name = name!;
        var resolution = NetworkIdentityResolver.Resolve(operation, state);
        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, resolution.FailureCategory);
    }

    [Fact]
    public void UnrelatedIdentityLoss_DoesNotBlockAddressConfiguration()
    {
        var state = MultiHomedPcFixture(new() { Subnet("unreadable", "", IoSystemFixture("unreadable", null)) });
        state.Devices.Add(Device("Other", Leaf("Other", NetworkInterface("Other", Node("unreadable", "")))));
        var resolution = NetworkIdentityResolver.Resolve(ConfigureRequest("op1", "PC_1", "N-PLC"), state);
        Assert.True(resolution.Success);
        Assert.Equal("N-PLC", resolution.Evidence!.NodeId);
    }

    // ---- Request builders --------------------------------------------------------------------

    private static NetworkOperationRequest ConfigureRequest(
        string operationId,
        string deviceName,
        string nodeId,
        NetworkSubnetTarget? subnet = null,
        NetworkIoSystemTarget? ioSystem = null) => new()
    {
        OperationId = operationId,
        Operation = "configure_network_device",
        Target = new NetworkObjectTarget { DeviceName = deviceName, NodeId = nodeId },
        Changes = new NetworkDeviceChanges
        {
            IpAddress = "192.168.0.99",
            Subnet = subnet,
            IoSystem = ioSystem,
        },
    };

    private static NetworkOperationRequest CreationRequest(string operationId, string deviceName) => new()
    {
        OperationId = operationId,
        Operation = "add_network_device",
        DeviceName = deviceName,
        TypeIdentifier = "OrderNumber:TEST",
    };

    // ---- Fixture builders ---------------------------------------------------------------------

    private static NodeInfo Node(string name, string nodeId) => new()
    {
        Name = name,
        NodeId = nodeId,
        NodeType = "Ethernet",
    };

    private static NetworkInterfaceInfo NetworkInterface(string name, params NodeInfo[] nodes) => new()
    {
        Name = name,
        Nodes = nodes.ToList(),
    };

    private static DeviceItemInfo Leaf(string name, NetworkInterfaceInfo networkInterface) => new()
    {
        Name = name,
        TypeIdentifier = "OrderNumber:TEST",
        NetworkInterfaces = new List<NetworkInterfaceInfo> { networkInterface },
        Items = new List<DeviceItemInfo>(),
    };

    private static DeviceItemInfo Branch(string name, params DeviceItemInfo[] children) => new()
    {
        Name = name,
        TypeIdentifier = "OrderNumber:TEST",
        NetworkInterfaces = new List<NetworkInterfaceInfo>(),
        Items = children.ToList(),
    };

    private static DeviceInfo Device(string name, params DeviceItemInfo[] items) => new()
    {
        Name = name,
        TypeIdentifier = "OrderNumber:PC-System",
        Items = items.ToList(),
    };

    private static SubnetInfo Subnet(string name, string subnetId, params IoSystemInfo[] ioSystems) => new()
    {
        Name = name,
        SubnetId = subnetId,
        NetworkType = "Ethernet",
        IoSystems = ioSystems.ToList(),
    };

    private static IoSystemInfo IoSystemFixture(string name, int? number) => new()
    {
        Name = name,
        Number = number,
    };

    /// <summary>
    /// One PC device ("PC_1") with a nested rack containing two device items, each exposing one
    /// network interface with one node: a PLC-facing node ("N-PLC") and a database-facing node
    /// ("N-DB"). Matches the brief's required fixture shape: one PC device, nested device items,
    /// two interfaces, a PLC-facing node and a database-facing node.
    /// </summary>
    private static HardwareConfigInfo MultiHomedPcFixture(List<SubnetInfo>? subnets = null) => new()
    {
        Devices = new List<DeviceInfo>
        {
            Device(
                "PC_1",
                Branch(
                    "Rack_0",
                    Leaf("PLC_Interface_Slot", NetworkInterface("PLC_Interface", Node("PLC_Node", "N-PLC"))),
                    Leaf("DB_Interface_Slot", NetworkInterface("DB_Interface", Node("DB_Node", "N-DB"))))),
        },
        Subnets = subnets ?? new List<SubnetInfo>(),
    };

    // ---- Device + node resolution -------------------------------------------------------------

    [Fact]
    public void Resolve_ConfigureNetworkDevice_ExactMatch_ResolvesCanonicalNodeEvidence()
    {
        var state = MultiHomedPcFixture();
        var operation = ConfigureRequest("op1", "PC_1", "N-PLC");

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.True(resolution.Success);
        var evidence = resolution.Evidence!;
        Assert.Equal("PC_1", evidence.DeviceName);
        Assert.Equal("OrderNumber:PC-System", evidence.DeviceTypeIdentifier);
        Assert.Equal(new[] { "Rack_0", "PLC_Interface_Slot" }, evidence.DeviceItemPath);
        Assert.Equal("PLC_Interface", evidence.NetworkInterfaceName);
        Assert.Equal("PLC_Node", evidence.NodeName);
        Assert.Equal("N-PLC", evidence.NodeId);
        Assert.Null(evidence.SubnetName);
        Assert.Null(evidence.SubnetId);
        Assert.Null(evidence.IoSystemName);
        Assert.Null(evidence.IoSystemNumber);
    }

    [Fact]
    public void Resolve_ConfigureNetworkDevice_ExactMatch_ResolvesTheDatabaseFacingNode()
    {
        var state = MultiHomedPcFixture();
        var operation = ConfigureRequest("op1", "PC_1", "N-DB");

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.True(resolution.Success);
        var evidence = resolution.Evidence!;
        Assert.Equal(new[] { "Rack_0", "DB_Interface_Slot" }, evidence.DeviceItemPath);
        Assert.Equal("DB_Interface", evidence.NetworkInterfaceName);
        Assert.Equal("DB_Node", evidence.NodeName);
        Assert.Equal("N-DB", evidence.NodeId);
    }

    [Fact]
    public void Resolve_ConfigureNetworkDevice_DeviceNameMatchIsCaseInsensitive()
    {
        var state = MultiHomedPcFixture();
        var operation = ConfigureRequest("op1", "pc_1", "N-PLC");

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.True(resolution.Success);
        Assert.Equal("PC_1", resolution.Evidence!.DeviceName);
    }

    [Fact]
    public void Resolve_ConfigureNetworkDevice_MissingDeviceName_FailsPostconditionFailed()
    {
        var state = MultiHomedPcFixture();
        var operation = ConfigureRequest("op1", "PC_404", "N-PLC");

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
        Assert.Contains("no device named", resolution.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_ConfigureNetworkDevice_DuplicateDeviceName_FailsPostconditionFailed()
    {
        var state = new HardwareConfigInfo
        {
            Devices = new List<DeviceInfo>
            {
                Device("PC_1", Branch("Rack_0", Leaf("if_1", NetworkInterface("if_1", Node("A", "N-A"))))),
                Device("PC_1", Branch("Rack_0", Leaf("if_1", NetworkInterface("if_1", Node("B", "N-B"))))),
            },
        };
        var operation = ConfigureRequest("op1", "PC_1", "N-A");

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
        Assert.Contains("multiple devices", resolution.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_ConfigureNetworkDevice_MissingNodeId_FailsPostconditionFailed()
    {
        var state = MultiHomedPcFixture();
        var operation = ConfigureRequest("op1", "PC_1", "N-404");

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
        Assert.Contains("no node with nodeid", resolution.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_ConfigureNetworkDevice_DuplicateNodeId_FailsPostconditionFailed()
    {
        var state = new HardwareConfigInfo
        {
            Devices = new List<DeviceInfo>
            {
                Device(
                    "PC_1",
                    Branch(
                        "Rack_0",
                        Leaf("PLC_Interface_Slot", NetworkInterface("PLC_Interface", Node("PLC_Node", "N-DUP"))),
                        Leaf("Extra_Slot", NetworkInterface("Extra_Interface", Node("Extra_Node", "N-DUP"))))),
            },
        };
        var operation = ConfigureRequest("op1", "PC_1", "N-DUP");

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
        Assert.Contains("multiple nodes", resolution.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_ConfigureNetworkDevice_UnreadableNodeIdNeverSatisfiesASelector()
    {
        // The node's own nodeId could not be read (modelled as empty, matching NodeInfo's
        // documented default). Even a request that names the same blank string must not match it:
        // an unreadable identity must never satisfy a write selector.
        var state = new HardwareConfigInfo
        {
            Devices = new List<DeviceInfo>
            {
                Device(
                    "PC_1",
                    Branch("Rack_0", Leaf("Unreadable_Slot", NetworkInterface("Unreadable_Interface", Node("Unreadable_Node", string.Empty))))),
            },
        };
        var operation = ConfigureRequest("op1", "PC_1", string.Empty);

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
    }

    [Fact]
    public void Resolve_ConfigureNetworkDevice_NoStateAvailable_FailsPostconditionFailed()
    {
        var operation = ConfigureRequest("op1", "PC_1", "N-PLC");

        var resolution = NetworkIdentityResolver.Resolve(operation, state: null);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
    }

    // ---- Subnet resolution ---------------------------------------------------------------------

    [Fact]
    public void Resolve_ConfigureNetworkDevice_SubnetExactMatch_PopulatesSubnetEvidence()
    {
        var state = MultiHomedPcFixture(new List<SubnetInfo>
        {
            Subnet("Subnet_A", "S-1"),
            Subnet("Subnet_B", "S-2"),
        });
        var operation = ConfigureRequest(
            "op1", "PC_1", "N-PLC", subnet: new NetworkSubnetTarget { SubnetId = "S-1" });

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.True(resolution.Success);
        Assert.Equal("Subnet_A", resolution.Evidence!.SubnetName);
        Assert.Equal("S-1", resolution.Evidence!.SubnetId);
    }

    [Fact]
    public void Resolve_ConfigureNetworkDevice_MissingSubnetId_FailsPostconditionFailed()
    {
        var state = MultiHomedPcFixture(new List<SubnetInfo> { Subnet("Subnet_A", "S-1") });
        var operation = ConfigureRequest(
            "op1", "PC_1", "N-PLC", subnet: new NetworkSubnetTarget { SubnetId = "S-404" });

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
        Assert.Contains("no subnet with subnetid", resolution.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_ConfigureNetworkDevice_DuplicateSubnetId_FailsPostconditionFailed()
    {
        var state = MultiHomedPcFixture(new List<SubnetInfo>
        {
            Subnet("Subnet_A", "S-DUP"),
            Subnet("Subnet_B", "S-DUP"),
        });
        var operation = ConfigureRequest(
            "op1", "PC_1", "N-PLC", subnet: new NetworkSubnetTarget { SubnetId = "S-DUP" });

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
        Assert.Contains("multiple subnets", resolution.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ---- IO-system resolution ------------------------------------------------------------------

    [Fact]
    public void Resolve_ConfigureNetworkDevice_IoSystemExactMatch_PopulatesIoSystemAndSubnetEvidence()
    {
        var state = MultiHomedPcFixture(new List<SubnetInfo>
        {
            Subnet("Subnet_A", "S-1", IoSystemFixture("IOSYS_1", 100)),
        });
        var operation = ConfigureRequest(
            "op1", "PC_1", "N-PLC",
            ioSystem: new NetworkIoSystemTarget { SubnetId = "S-1", Number = 100 });

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.True(resolution.Success);
        var evidence = resolution.Evidence!;
        Assert.Equal("Subnet_A", evidence.SubnetName);
        Assert.Equal("S-1", evidence.SubnetId);
        Assert.Equal("IOSYS_1", evidence.IoSystemName);
        Assert.Equal(100, evidence.IoSystemNumber);
    }

    [Fact]
    public void Resolve_ConfigureNetworkDevice_MissingIoSystemNumber_FailsPostconditionFailed()
    {
        var state = MultiHomedPcFixture(new List<SubnetInfo>
        {
            Subnet("Subnet_A", "S-1", IoSystemFixture("IOSYS_1", 100)),
        });
        var operation = ConfigureRequest(
            "op1", "PC_1", "N-PLC",
            ioSystem: new NetworkIoSystemTarget { SubnetId = "S-1", Number = 999 });

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
        Assert.Contains("no io system with number", resolution.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_ConfigureNetworkDevice_DuplicateIoSystemNumber_FailsPostconditionFailed()
    {
        var state = MultiHomedPcFixture(new List<SubnetInfo>
        {
            Subnet("Subnet_A", "S-1", IoSystemFixture("IOSYS_1", 5), IoSystemFixture("IOSYS_2", 5)),
        });
        var operation = ConfigureRequest(
            "op1", "PC_1", "N-PLC",
            ioSystem: new NetworkIoSystemTarget { SubnetId = "S-1", Number = 5 });

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
        Assert.Contains("multiple io systems", resolution.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Creation (add_network_device) ---------------------------------------------------------

    [Fact]
    public void Resolve_AddNetworkDevice_NeedsNoStateAndEvidencesRequestOnly()
    {
        var operation = CreationRequest("op1", "PLC_9");

        var resolution = NetworkIdentityResolver.Resolve(operation, state: null);

        Assert.True(resolution.Success);
        var evidence = resolution.Evidence!;
        Assert.Equal("PLC_9", evidence.DeviceName);
        Assert.Equal("OrderNumber:TEST", evidence.DeviceTypeIdentifier);
        Assert.Empty(evidence.DeviceItemPath);
        Assert.Null(evidence.NetworkInterfaceName);
        Assert.Null(evidence.NodeName);
        Assert.Null(evidence.NodeId);
        Assert.Null(evidence.SubnetName);
        Assert.Null(evidence.SubnetId);
        Assert.Null(evidence.IoSystemName);
        Assert.Null(evidence.IoSystemNumber);
    }
}
