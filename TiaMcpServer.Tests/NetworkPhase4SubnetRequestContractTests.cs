using System;
using System.Linq;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Phase 4 subnet lifecycle (create_subnet, update_subnet, delete_subnet): catalog registration
/// and all static request-shape/validation rules that do not require knowing the current subnet's
/// type (that check belongs to Task 2's target resolver).
/// </summary>
public class NetworkPhase4SubnetRequestContractTests
{
    [Fact]
    public void WriteCatalog_RegistersOnlyTheThreePhase4SubnetOperations()
    {
        var phase4 = NetworkOperationCatalog.WriteOperationNames
            .Where(name => name.EndsWith("_subnet", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(new[] { "create_subnet", "update_subnet", "delete_subnet" }, phase4);
    }

    private static NetworkValidationResult Validate(NetworkOperationRequest request)
        => NetworkOperationCatalog.ValidateWrite(new[] { request });

    private static NetworkOperationRequest CreateSubnetOp(
        string id = "create",
        NetworkSubnetDefinition? subnet = null,
        Action<NetworkOperationRequest>? adjust = null)
    {
        var request = new NetworkOperationRequest
        {
            OperationId = id,
            Operation = "create_subnet",
            Subnet = subnet ?? new NetworkSubnetDefinition { Name = "PN/IE_1", NetworkType = SubnetLifecycleContract.Ethernet },
        };
        adjust?.Invoke(request);
        return request;
    }

    private static NetworkOperationRequest UpdateSubnetOp(
        string id = "update",
        NetworkObjectTarget? target = null,
        NetworkSubnetChanges? changes = null,
        Action<NetworkOperationRequest>? adjust = null)
    {
        var request = new NetworkOperationRequest
        {
            OperationId = id,
            Operation = "update_subnet",
            Target = target ?? new NetworkObjectTarget { Kind = NetworkObjectKinds.Subnet, SubnetId = "S1" },
            SubnetChanges = changes ?? new NetworkSubnetChanges { Name = "Renamed" },
        };
        adjust?.Invoke(request);
        return request;
    }

    private static NetworkOperationRequest DeleteSubnetOp(
        string id = "delete",
        NetworkObjectTarget? target = null,
        Action<NetworkOperationRequest>? adjust = null)
    {
        var request = new NetworkOperationRequest
        {
            OperationId = id,
            Operation = "delete_subnet",
            Target = target ?? new NetworkObjectTarget { Kind = NetworkObjectKinds.Subnet, SubnetId = "S1" },
        };
        adjust?.Invoke(request);
        return request;
    }

    /// <summary>Builds a bare update/delete request with the given target, for selector-shape cases.</summary>
    private static NetworkOperationRequest TargetedSubnetOp(string operationName, NetworkObjectTarget? target)
    {
        var request = new NetworkOperationRequest { OperationId = "op", Operation = operationName, Target = target };
        if (operationName == "update_subnet")
        {
            request.SubnetChanges = new NetworkSubnetChanges { Name = "Renamed" };
        }

        return request;
    }

    private static void SetInapplicableField(NetworkOperationRequest request, string field)
    {
        switch (field)
        {
            case "target":
                request.Target = new NetworkObjectTarget { Kind = NetworkObjectKinds.Subnet, SubnetId = "S1" };
                break;
            case "subnet":
                request.Subnet = new NetworkSubnetDefinition { Name = "X", NetworkType = SubnetLifecycleContract.Ethernet };
                break;
            case "subnetChanges":
                request.SubnetChanges = new NetworkSubnetChanges { Name = "X" };
                break;
            case "changes":
                request.Changes = new NetworkDeviceChanges { IpAddress = "192.168.0.10" };
                break;
            case "typeIdentifier":
                request.TypeIdentifier = "OrderNumber:6ES7";
                break;
            case "deviceName":
                request.DeviceName = "PLC_1";
                break;
            case "deviceItemName":
                request.DeviceItemName = "PLC_1";
                break;
            case "objectKinds":
                request.ObjectKinds = new[] { NetworkObjectKinds.Subnet };
                break;
            case "pageSize":
                request.PageSize = 10;
                break;
            case "cursor":
                request.Cursor = "abc";
                break;
            case "attributeNames":
                request.AttributeNames = new[] { "Name" };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, "Unrecognised field for this helper.");
        }
    }

    // ------------------------------------------------------------------
    // Valid shapes
    // ------------------------------------------------------------------

    [Fact]
    public void ValidateWrite_AcceptsValidEthernetCreate()
    {
        var result = Validate(CreateSubnetOp());

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateWrite_AcceptsValidProfibusCreateWithBothOptionalAttributes()
    {
        var result = Validate(CreateSubnetOp(subnet: new NetworkSubnetDefinition
        {
            Name = "PB_1",
            NetworkType = SubnetLifecycleContract.Profibus,
            HighestAddress = 31,
            TransmissionSpeed = "Baud1500000",
        }));

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateWrite_AcceptsValidRenameOnlyUpdate()
    {
        var result = Validate(UpdateSubnetOp());

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateWrite_AcceptsValidProfibusAttributeUpdate()
    {
        var result = Validate(UpdateSubnetOp(
            changes: new NetworkSubnetChanges { HighestAddress = 16, TransmissionSpeed = "Baud500000" }));

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateWrite_AcceptsValidDelete()
    {
        var result = Validate(DeleteSubnetOp());

        Assert.True(result.IsValid, result.Error);
    }

    // ------------------------------------------------------------------
    // Create: name and network type
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateWrite_RejectsMissingOrBlankCreateName(string? name)
    {
        var result = Validate(CreateSubnetOp(
            subnet: new NetworkSubnetDefinition { Name = name, NetworkType = SubnetLifecycleContract.Ethernet }));

        Assert.False(result.IsValid);
        Assert.Contains("subnet.name", result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Foo")]
    public void ValidateWrite_RejectsMissingOrUnknownCreateNetworkType(string? networkType)
    {
        var result = Validate(CreateSubnetOp(
            subnet: new NetworkSubnetDefinition { Name = "PN/IE_1", NetworkType = networkType }));

        Assert.False(result.IsValid);
        Assert.Contains("networkType", result.Error);
    }

    [Theory]
    [InlineData("highestAddress")]
    [InlineData("transmissionSpeed")]
    public void ValidateWrite_RejectsEthernetCreateCarryingEitherProfibusAttribute(string field)
    {
        var subnet = new NetworkSubnetDefinition { Name = "PN/IE_1", NetworkType = SubnetLifecycleContract.Ethernet };
        if (field == "highestAddress")
        {
            subnet.HighestAddress = 10;
        }
        else
        {
            subnet.TransmissionSpeed = "Baud500000";
        }

        var result = Validate(CreateSubnetOp(subnet: subnet));

        Assert.False(result.IsValid);
        Assert.Contains(field, result.Error);
    }

    // ------------------------------------------------------------------
    // Update: subnetChanges shape
    // ------------------------------------------------------------------

    [Fact]
    public void ValidateWrite_RejectsEmptySubnetChanges()
    {
        var result = Validate(UpdateSubnetOp(changes: new NetworkSubnetChanges()));

        Assert.False(result.IsValid);
        Assert.Contains("at least one change", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateWrite_RejectsBlankUpdateName(string name)
    {
        var result = Validate(UpdateSubnetOp(changes: new NetworkSubnetChanges { Name = name }));

        Assert.False(result.IsValid);
        Assert.Contains("subnetChanges.name", result.Error);
    }

    // ------------------------------------------------------------------
    // Numeric range and enum vocabulary (create and update)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(-1)]
    [InlineData(127)]
    public void ValidateWrite_RejectsHighestAddressOutOfRange_OnCreate(int value)
    {
        var result = Validate(CreateSubnetOp(subnet: new NetworkSubnetDefinition
        {
            Name = "PB_1",
            NetworkType = SubnetLifecycleContract.Profibus,
            HighestAddress = value,
        }));

        Assert.False(result.IsValid);
        Assert.Contains("highestAddress", result.Error);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(127)]
    public void ValidateWrite_RejectsHighestAddressOutOfRange_OnUpdate(int value)
    {
        var result = Validate(UpdateSubnetOp(changes: new NetworkSubnetChanges { HighestAddress = value }));

        Assert.False(result.IsValid);
        Assert.Contains("highestAddress", result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(126)]
    public void ValidateWrite_AcceptsHighestAddressAtRangeBoundaries(int value)
    {
        var result = Validate(CreateSubnetOp(subnet: new NetworkSubnetDefinition
        {
            Name = "PB_1",
            NetworkType = SubnetLifecycleContract.Profibus,
            HighestAddress = value,
        }));

        Assert.True(result.IsValid, result.Error);
    }

    [Theory]
    [InlineData("Baud9600")]
    [InlineData("Baud19200")]
    [InlineData("Baud45450")]
    [InlineData("Baud93750")]
    [InlineData("Baud187500")]
    [InlineData("Baud500000")]
    [InlineData("Baud1500000")]
    [InlineData("Baud3000000")]
    [InlineData("Baud6000000")]
    [InlineData("Baud12000000")]
    public void ValidateWrite_AcceptsEveryAcceptedBaudSymbol(string speed)
    {
        var result = Validate(CreateSubnetOp(subnet: new NetworkSubnetDefinition
        {
            Name = "PB_1",
            NetworkType = SubnetLifecycleContract.Profibus,
            TransmissionSpeed = speed,
        }));

        Assert.True(result.IsValid, result.Error);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Baud9375")]
    [InlineData("baud9600")]
    public void ValidateWrite_RejectsUnsupportedBaudSymbols(string speed)
    {
        var result = Validate(CreateSubnetOp(subnet: new NetworkSubnetDefinition
        {
            Name = "PB_1",
            NetworkType = SubnetLifecycleContract.Profibus,
            TransmissionSpeed = speed,
        }));

        Assert.False(result.IsValid);
        Assert.Contains("transmissionSpeed", result.Error);
    }

    // ------------------------------------------------------------------
    // Update/delete target selector shape
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void ValidateWrite_RejectsMissingTarget(string operationName)
    {
        var result = Validate(TargetedSubnetOp(operationName, target: null));

        Assert.False(result.IsValid);
        Assert.Contains("target", result.Error);
    }

    [Theory]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void ValidateWrite_RejectsWrongKindTarget(string operationName)
    {
        var result = Validate(TargetedSubnetOp(
            operationName,
            target: new NetworkObjectTarget { Kind = NetworkObjectKinds.Node, DeviceName = "PLC_1", NodeId = "7" }));

        Assert.False(result.IsValid);
        Assert.Contains("kind", result.Error);
    }

    [Theory]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void ValidateWrite_RejectsBlankSubnetIdTarget(string operationName)
    {
        var result = Validate(TargetedSubnetOp(
            operationName,
            target: new NetworkObjectTarget { Kind = NetworkObjectKinds.Subnet, SubnetId = "   " }));

        Assert.False(result.IsValid);
        Assert.Contains("subnetId", result.Error);
    }

    [Theory]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void ValidateWrite_RejectsExtraSelectorMembersOnTarget(string operationName)
    {
        var result = Validate(TargetedSubnetOp(
            operationName,
            target: new NetworkObjectTarget { Kind = NetworkObjectKinds.Subnet, SubnetId = "S1", DeviceName = "PLC_1" }));

        Assert.False(result.IsValid);
        Assert.Contains("deviceName", result.Error);
    }

    // ------------------------------------------------------------------
    // Inapplicable top-level fields per operation
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("target")]
    [InlineData("subnetChanges")]
    [InlineData("changes")]
    [InlineData("typeIdentifier")]
    [InlineData("deviceName")]
    [InlineData("deviceItemName")]
    [InlineData("objectKinds")]
    [InlineData("pageSize")]
    [InlineData("cursor")]
    [InlineData("attributeNames")]
    public void ValidateWrite_RejectsInapplicableFieldsOnCreateSubnet(string field)
    {
        var request = CreateSubnetOp();
        SetInapplicableField(request, field);

        var result = Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(field, result.Error);
    }

    [Theory]
    [InlineData("subnet")]
    [InlineData("changes")]
    [InlineData("typeIdentifier")]
    [InlineData("deviceName")]
    [InlineData("deviceItemName")]
    [InlineData("objectKinds")]
    [InlineData("pageSize")]
    [InlineData("cursor")]
    [InlineData("attributeNames")]
    public void ValidateWrite_RejectsInapplicableFieldsOnUpdateSubnet(string field)
    {
        var request = UpdateSubnetOp();
        SetInapplicableField(request, field);

        var result = Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(field, result.Error);
    }

    [Theory]
    [InlineData("subnet")]
    [InlineData("subnetChanges")]
    [InlineData("changes")]
    [InlineData("typeIdentifier")]
    [InlineData("deviceName")]
    [InlineData("deviceItemName")]
    [InlineData("objectKinds")]
    [InlineData("pageSize")]
    [InlineData("cursor")]
    [InlineData("attributeNames")]
    public void ValidateWrite_RejectsInapplicableFieldsOnDeleteSubnet(string field)
    {
        var request = DeleteSubnetOp();
        SetInapplicableField(request, field);

        var result = Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(field, result.Error);
    }
}
