using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using Xunit;

namespace TiaMcpServer.Tests.Network;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class NetworkGuardedWriteOrderingTests
{
    [Fact]
    public async Task DifferentDeviceCasing_RepeatedSettingsSupersedeWithoutChangingImmediateIdentity()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var second = NetworkGuardedWriteFixture.Configure("second", "10.0.0.2");
        second.Target!.DeviceName = "plc_grouped";
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("first", "10.0.0.1"), second);
        Assert.All(response.Verification!.Operations, operation => Assert.Equal("passed", operation.Status));
        Assert.Equal("PLC_Grouped", response.Verification.Operations[0].Evidence!.Identity["deviceName"]);
        Assert.Equal("plc_grouped", response.Verification.Operations[1].Evidence!.Identity["deviceName"]);
        Assert.True(response.Success);
        var address = Assert.Single(response.Verification.FinalChecks, check => check.Name.EndsWith("/Address"));
        Assert.Equal("10.0.0.2", address.Expected);
        Assert.Equal("passed", address.Status);
        Assert.Single(response.Verification.FinalChecks, check => check.Name.EndsWith("/node-2/exists"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DifferentDeviceCasing_DeleteSupersedesConnectedRelationships(bool includeIo)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-io-move");
        var connect = NetworkGuardedWriteFixture.Configure("connect", connect: true);
        connect.Target!.DeviceName = "plc_grouped";
        connect.Changes = new()
        {
            Subnet = new() { SubnetId = "subnet-1" },
            IoSystem = includeIo ? new() { SubnetId = "subnet-1", Number = 1 } : null
        };
        var response = await fixture.RunAsync(false, connect, NetworkGuardedWriteFixture.Delete());
        Assert.All(response.Verification!.Operations, operation => Assert.Equal("passed", operation.Status));
        Assert.Equal("plc_grouped", response.Verification.Operations[0].Evidence!.Identity["deviceName"]);
        Assert.Contains(response.Effects[1].Effect!.AffectedNodes, node => node.DeviceName == "PLC_Grouped");
        Assert.True(response.Success);
        Assert.DoesNotContain(response.Verification.FinalChecks, check => check.Name.EndsWith("/Subnet") || check.Name.EndsWith("/IoSystem"));
        Assert.Single(response.Verification.FinalChecks, check => check.Name.EndsWith("/node-2/exists"));
        Assert.Contains(response.Verification.FinalChecks, check => check.Name.EndsWith("/node-3/exists") && check.Status == "passed");
    }

    [Fact]
    public async Task RepeatedSettings_PreserveImmediateChecks()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("first", "10.0.0.1"), NetworkGuardedWriteFixture.Configure("second", "10.0.0.2"));
        Assert.True(response.Success);
        Assert.Equal("passed", response.Verification!.Operations[0].Evidence!.Checks[0].Status);
        Assert.Equal("10.0.0.1", response.Verification.Operations[0].Evidence!.Checks[0].Expected);
        Assert.True(response.Verification.Success);
        Assert.Contains(response.Verification.FinalChecks, c => c.Expected == "10.0.0.2" && c.Status == "passed");
    }
    [Fact]
    public async Task ConnectThenDelete_ReplansLateInfo()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-disconnected");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("connect", connect: true), NetworkGuardedWriteFixture.Delete());
        Assert.True(response.Success);
        Assert.Single(response.Guards, g => g.Id == "network_delete_connected_subnet");
        Assert.Single(response.Effects[1].Effect!.AffectedNodes);
        Assert.Contains(response.Verification!.FinalChecks, c => c.Name.Contains("node-2") && c.Status == "passed");
    }
    [Fact]
    public async Task EarlierAdd_RefreshesRootCount()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var add = new NetworkOperationRequest { OperationId = "add", Operation = "add_network_device", DeviceName = "new", TypeIdentifier = "OrderNumber:TEST" };
        var response = await fixture.RunAsync(false, add, NetworkGuardedWriteFixture.Delete());
        Assert.True(response.Success);
        Assert.Equal(3, response.Effects[1].Effect!.RootDeviceCount);
    }
    [Fact]
    public async Task LateBlock_PreservesEarlierChanges()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-late-block");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("first", "10.0.0.1"), NetworkGuardedWriteFixture.Delete(), NetworkGuardedWriteFixture.Configure("last", "10.0.0.3"));
        Assert.Equal("applied", response.Phase);
        Assert.False(response.Success);
        Assert.Null(response.Error);
        Assert.Equal("succeeded", response.Batch!.Operations[0].Status);
        Assert.Equal("earlierOperationFailed", response.Batch.Operations[2].SkipReason);
        Assert.Contains(response.Guards, g => g.Id == "network_state_unverifiable");
        Assert.Single(response.Verification!.Operations);
        Assert.Equal("first", response.Verification.Operations[0].OperationId);
        Assert.Contains(response.Verification.FinalChecks, c => c.Expected == "10.0.0.1" && c.Status == "passed");
    }

    [Fact]
    public async Task PartialConfiguration_VerifiesOnlyAppliedSubsetAndStops()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-partial");
        var partial = NetworkGuardedWriteFixture.Configure("partial", "10.0.0.5");
        partial.Changes = new() { IpAddress = "10.0.0.5", IoSystem = new() { SubnetId = "subnet-1", Number = 1 } };
        var response = await fixture.RunAsync(false, partial, NetworkGuardedWriteFixture.Delete());
        Assert.False(response.Success);
        Assert.Equal("worker_operation_failed", response.Batch!.Operations[0].Failure!.Category);
        Assert.NotNull(response.Batch.Operations[0].Result);
        Assert.Equal("earlierOperationFailed", response.Batch.Operations[1].SkipReason);
        Assert.True(response.Verification!.Success);
        Assert.Single(response.Verification.Operations);
        Assert.DoesNotContain(response.Verification.FinalChecks, c => c.Name.EndsWith("/IoSystem"));
    }
    [Fact]
    public async Task DeletedSubnet_AggregatePassCannotHideLostUngroupedNode()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-lost-node");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Delete());
        Assert.False(response.Success);
        Assert.Equal("passed", response.Verification!.Operations[0].Status);
        Assert.Contains(response.Verification.FinalChecks, c => c.Name.Contains("PLC_Ungrouped") && c.Status == "failed");
    }
    [Fact]
    public async Task PostReadFailure_IsUnverifiedAndNeverReplayed()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-postread-failure");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("first", "10.0.0.1"));
        Assert.False(response.Success);
        Assert.Contains(response.Verification!.FinalChecks, c => c.Status == "unverified");
        Assert.Contains(response.Warnings, w => w.Contains("do not replay"));
        Assert.Single(requests.Methods(), m => m == "configure_network_device");
    }
    [Fact]
    public async Task ReplanningOccursBetweenDispatches()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("first", "10.0.0.1"), NetworkGuardedWriteFixture.Configure("second", "10.0.0.2"));
        var relevant = requests.Methods().Where(m => m is "read_hardware_config" or "configure_network_device").ToArray();
        Assert.Equal(new[] { "read_hardware_config", "read_hardware_config", "configure_network_device", "read_hardware_config", "configure_network_device", "read_hardware_config" }, relevant);
    }
    [Fact]
    public async Task ReturnedPlanCannotMutateRetainedInventory()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var domain = new NetworkWriteDomain(fixture.Client);
        var operation = NetworkGuardedWriteFixture.Delete();
        var plan = await domain.PlanAsync("network-guarded", new[] { operation });
        plan.Items[0].Effect!.AffectedNodes[0].NodeId = "caller-corruption";
        var projected = domain.Project(operation, await domain.MutateAsync("network-guarded", operation));
        var verification = await domain.VerifyAsync("network-guarded", StructuredOperationBatch.FromItems(new[] { projected }));
        Assert.True(verification!.Success);
        Assert.DoesNotContain(verification.FinalChecks, c => c.Name.Contains("caller-corruption"));
        Assert.Contains(verification.FinalChecks, c => c.Name.Contains("node-2"));
    }
    [Fact]
    public async Task AddedDevice_VerifiesExactNestedItemRatherThanDeviceType()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var response = await fixture.RunAsync(false, new NetworkOperationRequest { OperationId = "add", Operation = "add_network_device", DeviceName = "new", DeviceItemName = "CPU", TypeIdentifier = "OrderNumber:TEST" });
        Assert.True(response.Success);
        Assert.Contains(response.Verification!.FinalChecks, c => c.Name.EndsWith("/typeIdentifier") && c.Status == "passed");
    }
    [Fact]
    public async Task UnknownAttempt_RetainsUnverifiedRecordAndSkipsFollowingItems()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-unknown-result");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("unknown", "10.0.0.8"), NetworkGuardedWriteFixture.Delete());
        Assert.False(response.Success);
        Assert.Equal("unverified", Assert.Single(response.Verification!.Operations).Status);
        Assert.Null(response.Verification.Operations[0].Evidence);
        Assert.Equal("earlierOperationFailed", response.Batch!.Operations[1].SkipReason);
        Assert.Single(requests.Methods(), m => m == "configure_network_device");
        Assert.DoesNotContain("delete_subnet", requests.Methods());
    }
    [Fact]
    public async Task SubnetUpdateThenDelete_SupersedesExistenceAndName()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var rename = new NetworkOperationRequest { OperationId = "rename", Operation = "update_subnet", Target = new() { Kind = "subnet", SubnetId = "subnet-1" }, SubnetChanges = new() { Name = "Renamed" } };
        var response = await fixture.RunAsync(false, rename, NetworkGuardedWriteFixture.Delete());
        Assert.True(response.Success);
        Assert.Equal(2, response.Verification!.Operations.Count);
        Assert.DoesNotContain(response.Verification.FinalChecks, c => c.Name.EndsWith("/Name") || c.Name == "subnet/subnet-1//exists");
        Assert.Contains(response.Verification.FinalChecks, c => c.Name.EndsWith("/absent") && c.Status == "passed");
    }
    [Fact]
    public async Task ProfibusAttributes_UseTypedInspectionAndEnumSymbol()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var create = new NetworkOperationRequest { OperationId = "create", Operation = "create_subnet", Subnet = new() { Name = "PB", NetworkType = "Profibus", HighestAddress = 31, TransmissionSpeed = "Baud187500" } };
        var created = await fixture.RunAsync(false, create);
        Assert.True(created.Success);
        var id = created.Batch!.Operations[0].Result!.Value.GetProperty("subnetId").GetString();
        var update = new NetworkOperationRequest { OperationId = "update", Operation = "update_subnet", Target = new() { Kind = "subnet", SubnetId = id }, SubnetChanges = new() { HighestAddress = 63, TransmissionSpeed = "Baud1500000" } };
        var updated = await fixture.RunAsync(false, update);
        Assert.True(updated.Success);
        Assert.Equal("available", updated.Effects[0].Effect!.CurrentSettings["HighestAddress"].Availability);
        Assert.Contains(updated.Verification!.FinalChecks, c => c.Expected == "63" && c.Status == "passed");
    }
    [Fact]
    public async Task SubnetOnlyMove_DoesNotInferSupersessionOfEarlierExplicitIoTuple()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-io-move");
        var attach = NetworkGuardedWriteFixture.Configure("attach");
        attach.Changes = new() { IoSystem = new() { SubnetId = "subnet-1", Number = 1 } };
        var move = NetworkGuardedWriteFixture.Configure("move");
        move.Changes = new() { Subnet = new() { SubnetId = "subnet-2" } };
        var response = await fixture.RunAsync(false, attach, move);
        Assert.False(response.Success);
        Assert.All(response.Verification!.Operations, operation => Assert.Equal("passed", operation.Status));
        var io = Assert.Single(response.Verification.FinalChecks, c => c.Name.EndsWith("/IoSystem"));
        Assert.Equal("failed", io.Status);
        Assert.Equal("[\"subnet-1\",1]", io.Expected);
        Assert.Null(io.Observed);
        Assert.Contains("side effects", io.Message);
    }
    [Fact]
    public async Task ReplannedRootCount_DoesNotEraseEarlierPreservationExpectation()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-root-drift");
        var rename = new NetworkOperationRequest { OperationId = "rename", Operation = "update_subnet", Target = new() { Kind = "subnet", SubnetId = "subnet-1" }, SubnetChanges = new() { Name = "Renamed" } };
        var response = await fixture.RunAsync(false, rename, NetworkGuardedWriteFixture.Delete());
        Assert.False(response.Success);
        Assert.Equal(1, response.Effects[1].Effect!.RootDeviceCount);
        Assert.Contains(response.Verification!.FinalChecks, c => c.Name == "networkDeviceCountUnchanged" && c.Expected == "2" && c.Observed == "1" && c.Status == "failed");
    }
    [Fact]
    public async Task UnknownDelete_StillInspectsExactPlannedNodes()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-unknown-result");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Delete());
        Assert.False(response.Success);
        Assert.Equal("unverified", Assert.Single(response.Verification!.Operations).Status);
        Assert.Contains(response.Verification.FinalChecks, c => c.Name == "node/PLC_Grouped/node-2/exists" && c.Status == "passed");
        Assert.Contains(response.Verification.FinalChecks, c => c.Name == "node/PLC_Ungrouped/node-3/exists" && c.Status == "passed");
    }
}
