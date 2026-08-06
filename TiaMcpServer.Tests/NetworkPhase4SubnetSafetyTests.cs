using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Pure, Siemens-free tests of <see cref="NetworkIdentityResolver"/> for the Phase 4 subnet
/// lifecycle operations (<c>create_subnet</c>, <c>update_subnet</c>, <c>delete_subnet</c>).
///
/// <para>
/// Update/delete resolve by exact ordinal (<see cref="System.StringComparison.Ordinal"/>)
/// <c>subnetId</c> match only. There is no name fallback, no case-insensitive match, and no
/// first-match/index fallback. Connected node names and IO-system contents are never consulted:
/// they must not change resolution and must never become a deletion blocker.
/// </para>
/// </summary>
public class NetworkPhase4SubnetSafetyTests
{
    // ---- Request builders --------------------------------------------------------------------

    private static NetworkOperationRequest CreateSubnetRequest(
        string operationId,
        string name,
        string networkType,
        int? highestAddress = null,
        string? transmissionSpeed = null) => new()
    {
        OperationId = operationId,
        Operation = "create_subnet",
        Subnet = new NetworkSubnetDefinition
        {
            Name = name,
            NetworkType = networkType,
            HighestAddress = highestAddress,
            TransmissionSpeed = transmissionSpeed,
        },
    };

    private static NetworkOperationRequest UpdateSubnetRequest(
        string operationId,
        string subnetId,
        NetworkSubnetChanges changes) => new()
    {
        OperationId = operationId,
        Operation = "update_subnet",
        Target = new NetworkObjectTarget { Kind = NetworkObjectKinds.Subnet, SubnetId = subnetId },
        SubnetChanges = changes,
    };

    private static NetworkOperationRequest DeleteSubnetRequest(string operationId, string subnetId) => new()
    {
        OperationId = operationId,
        Operation = "delete_subnet",
        Target = new NetworkObjectTarget { Kind = NetworkObjectKinds.Subnet, SubnetId = subnetId },
    };

    // ---- Fixture builders ---------------------------------------------------------------------

    private static SubnetInfo Subnet(
        string name,
        string subnetId,
        string? networkType = "Ethernet",
        List<string>? connectedNodeNames = null,
        List<IoSystemInfo>? ioSystems = null) => new()
    {
        Name = name,
        SubnetId = subnetId,
        NetworkType = networkType,
        ConnectedNodeNames = connectedNodeNames ?? new List<string>(),
        IoSystems = ioSystems ?? new List<IoSystemInfo>(),
    };

    private static HardwareConfigInfo State(params SubnetInfo[] subnets) => new()
    {
        Subnets = subnets.ToList(),
    };

    // ---- create_subnet: request-derived evidence, no state consulted --------------------------

    [Fact]
    public void Resolve_CreateSubnet_ResolvesRequestDerivedEvidenceWithNoInventedId()
    {
        var operation = CreateSubnetRequest("op1", "LINE_1", SubnetLifecycleContract.Ethernet);

        var resolution = NetworkIdentityResolver.Resolve(operation, state: null);

        Assert.True(resolution.Success);
        var evidence = resolution.Evidence!;
        Assert.Null(evidence.DeviceName);
        Assert.Null(evidence.DeviceTypeIdentifier);
        Assert.Empty(evidence.DeviceItemPath);
        Assert.Null(evidence.NetworkInterfaceName);
        Assert.Null(evidence.NodeName);
        Assert.Null(evidence.NodeId);
        Assert.Equal("LINE_1", evidence.SubnetName);
        Assert.Null(evidence.SubnetId);
        Assert.Null(evidence.IoSystemName);
        Assert.Null(evidence.IoSystemNumber);
    }

    [Fact]
    public void Resolve_CreateSubnet_NeedsNoHardwareSnapshot()
    {
        var operation = CreateSubnetRequest("op1", "LINE_1", SubnetLifecycleContract.Ethernet);
        var state = State(); // present but irrelevant; creation never reads it

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.True(resolution.Success);
        Assert.Equal("LINE_1", resolution.Evidence!.SubnetName);
    }

    // ---- update_subnet: exact ordinal subnetId match -------------------------------------------

    [Fact]
    public void Resolve_UpdateSubnet_ExactOrdinalMatch_ResolvesCurrentNameAndId()
    {
        var state = State(Subnet("LINE_1", "S-1"), Subnet("LINE_2", "S-2"));
        var operation = UpdateSubnetRequest("op1", "S-1", new NetworkSubnetChanges { Name = "LINE_1_RENAMED" });

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.True(resolution.Success);
        var evidence = resolution.Evidence!;
        Assert.Null(evidence.DeviceName);
        Assert.Equal("LINE_1", evidence.SubnetName);
        Assert.Equal("S-1", evidence.SubnetId);
    }

    [Fact]
    public void Resolve_UpdateSubnet_DifferentCasedId_DoesNotMatch()
    {
        var state = State(Subnet("LINE_1", "S-1"));
        var operation = UpdateSubnetRequest("op1", "s-1", new NetworkSubnetChanges { Name = "LINE_1_RENAMED" });

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
    }

    [Fact]
    public void Resolve_UpdateSubnet_ZeroMatches_FailsPostconditionFailed()
    {
        var state = State(Subnet("LINE_1", "S-1"));
        var operation = UpdateSubnetRequest("op1", "S-404", new NetworkSubnetChanges { Name = "X" });

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
    }

    [Fact]
    public void Resolve_UpdateSubnet_DuplicateIds_FailsPostconditionFailed()
    {
        var state = State(Subnet("LINE_1", "S-DUP"), Subnet("LINE_2", "S-DUP"));
        var operation = UpdateSubnetRequest("op1", "S-DUP", new NetworkSubnetChanges { Name = "X" });

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
    }

    [Fact]
    public void Resolve_UpdateSubnet_BlankNetworkType_FailsPostconditionFailed()
    {
        var state = State(Subnet("LINE_1", "S-1", networkType: null));
        var operation = UpdateSubnetRequest("op1", "S-1", new NetworkSubnetChanges { Name = "X" });

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
    }

    [Fact]
    public void Resolve_UpdateSubnet_UnsupportedNetworkType_FailsPostconditionFailed()
    {
        var state = State(Subnet("LINE_1", "S-1", networkType: "Bluetooth"));
        var operation = UpdateSubnetRequest("op1", "S-1", new NetworkSubnetChanges { Name = "X" });

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
    }

    [Fact]
    public void Resolve_UpdateSubnet_EthernetRename_Succeeds()
    {
        var state = State(Subnet("LINE_1", "S-1", networkType: SubnetLifecycleContract.Ethernet));
        var operation = UpdateSubnetRequest("op1", "S-1", new NetworkSubnetChanges { Name = "LINE_1_RENAMED" });

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.True(resolution.Success);
        Assert.Equal("LINE_1", resolution.Evidence!.SubnetName);
    }

    [Fact]
    public void Resolve_UpdateSubnet_EthernetWithHighestAddress_RejectedAsValidationError()
    {
        var state = State(Subnet("LINE_1", "S-1", networkType: SubnetLifecycleContract.Ethernet));
        var operation = UpdateSubnetRequest("op1", "S-1", new NetworkSubnetChanges { HighestAddress = 10 });

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.ValidationError, resolution.FailureCategory);
    }

    [Fact]
    public void Resolve_UpdateSubnet_EthernetWithTransmissionSpeed_RejectedAsValidationError()
    {
        var state = State(Subnet("LINE_1", "S-1", networkType: SubnetLifecycleContract.Ethernet));
        var operation = UpdateSubnetRequest("op1", "S-1", new NetworkSubnetChanges { TransmissionSpeed = "Baud500000" });

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.ValidationError, resolution.FailureCategory);
    }

    [Fact]
    public void Resolve_UpdateSubnet_ProfibusNameAndAttributeUpdate_Succeeds()
    {
        var state = State(Subnet("BUS_1", "S-1", networkType: SubnetLifecycleContract.Profibus));
        var operation = UpdateSubnetRequest(
            "op1",
            "S-1",
            new NetworkSubnetChanges { Name = "BUS_1_RENAMED", HighestAddress = 16, TransmissionSpeed = "Baud500000" });

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.True(resolution.Success);
        Assert.Equal("BUS_1", resolution.Evidence!.SubnetName);
        Assert.Equal("S-1", resolution.Evidence!.SubnetId);
    }

    [Fact]
    public void Resolve_UpdateSubnet_ConnectedNodeNamesAndIoSystems_DoNotChangeResolution()
    {
        var connected = State(Subnet(
            "LINE_1",
            "S-1",
            networkType: SubnetLifecycleContract.Ethernet,
            connectedNodeNames: new List<string> { "PLC_1.X1", "PLC_2.X1" },
            ioSystems: new List<IoSystemInfo> { new() { Name = "IOSYS_1", Number = 100 } }));
        var bare = State(Subnet("LINE_1", "S-1", networkType: SubnetLifecycleContract.Ethernet));
        var operation = UpdateSubnetRequest("op1", "S-1", new NetworkSubnetChanges { Name = "LINE_1_RENAMED" });

        var connectedResolution = NetworkIdentityResolver.Resolve(operation, connected);
        var bareResolution = NetworkIdentityResolver.Resolve(operation, bare);

        Assert.True(connectedResolution.Success);
        Assert.True(bareResolution.Success);
        Assert.Equal(bareResolution.Evidence!.SubnetName, connectedResolution.Evidence!.SubnetName);
        Assert.Equal(bareResolution.Evidence!.SubnetId, connectedResolution.Evidence!.SubnetId);
    }

    [Fact]
    public void Resolve_UpdateSubnet_NoStateAvailable_FailsPostconditionFailed()
    {
        var operation = UpdateSubnetRequest("op1", "S-1", new NetworkSubnetChanges { Name = "X" });

        var resolution = NetworkIdentityResolver.Resolve(operation, state: null);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
    }

    // ---- delete_subnet: exact ordinal subnetId match, no dependency inventory -----------------

    [Fact]
    public void Resolve_DeleteSubnet_ExactOrdinalMatch_ResolvesCurrentNameAndId()
    {
        var state = State(Subnet("LINE_1", "S-1"), Subnet("LINE_2", "S-2"));
        var operation = DeleteSubnetRequest("op1", "S-1");

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.True(resolution.Success);
        var evidence = resolution.Evidence!;
        Assert.Null(evidence.DeviceName);
        Assert.Equal("LINE_1", evidence.SubnetName);
        Assert.Equal("S-1", evidence.SubnetId);
    }

    [Fact]
    public void Resolve_DeleteSubnet_DifferentCasedId_DoesNotMatch()
    {
        var state = State(Subnet("LINE_1", "S-1"));
        var operation = DeleteSubnetRequest("op1", "s-1");

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
    }

    [Fact]
    public void Resolve_DeleteSubnet_ZeroMatches_FailsPostconditionFailed()
    {
        var state = State(Subnet("LINE_1", "S-1"));
        var operation = DeleteSubnetRequest("op1", "S-404");

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
    }

    [Fact]
    public void Resolve_DeleteSubnet_DuplicateIds_FailsPostconditionFailed()
    {
        var state = State(Subnet("LINE_1", "S-DUP"), Subnet("LINE_2", "S-DUP"));
        var operation = DeleteSubnetRequest("op1", "S-DUP");

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
    }

    [Fact]
    public void Resolve_DeleteSubnet_ConnectedSubnet_ResolvesWithoutDependencyBlock()
    {
        // Connected node names and a non-empty IO-system collection must never block deletion
        // resolution: there is no dependency inventory in this resolver.
        var state = State(Subnet(
            "LINE_1",
            "S-1",
            networkType: SubnetLifecycleContract.Ethernet,
            connectedNodeNames: new List<string> { "PLC_1.X1" },
            ioSystems: new List<IoSystemInfo> { new() { Name = "IOSYS_1", Number = 100 } }));
        var operation = DeleteSubnetRequest("op1", "S-1");

        var resolution = NetworkIdentityResolver.Resolve(operation, state);

        Assert.True(resolution.Success);
        Assert.Equal("LINE_1", resolution.Evidence!.SubnetName);
        Assert.Equal("S-1", resolution.Evidence!.SubnetId);
    }

    [Fact]
    public void Resolve_DeleteSubnet_NoStateAvailable_FailsPostconditionFailed()
    {
        var operation = DeleteSubnetRequest("op1", "S-1");

        var resolution = NetworkIdentityResolver.Resolve(operation, state: null);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
    }
}
