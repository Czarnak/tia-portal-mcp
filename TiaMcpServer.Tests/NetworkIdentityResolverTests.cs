using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Pure, Siemens-free tests of <see cref="NetworkIdentityResolver"/>: exact match, duplicate,
/// missing, and unreadable identity variants for device, node, subnet, and IO-system resolution.
/// Every fixture is a synthetic <see cref="HardwareConfigInfo"/> — no worker, no TIA Portal.
/// </summary>
public class NetworkIdentityResolverTests
{
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
        Target = new NetworkDeviceTarget { DeviceName = deviceName, NodeId = nodeId },
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
