using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class NetworkGuardedWriteOrderingTests
{
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
        Assert.Single(response.Guards.Where(g => g.Id == "network_delete_connected_subnet"));
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
    }
}
