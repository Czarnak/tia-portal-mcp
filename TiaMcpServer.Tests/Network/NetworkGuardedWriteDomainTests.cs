using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Network;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class NetworkGuardedWriteDomainTests
{
    [Fact]
    public async Task DryRun_ReportsBlocksWithoutMutation()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-incomplete");
        var response = await fixture.RunAsync(true, NetworkGuardedWriteFixture.Delete());
        Assert.Equal("preview", response.Phase);
        Assert.True(response.Success);
        Assert.Contains(response.Guards, g => g.Id == "network_state_unverifiable" && g.Severity == "block");
        Assert.Null(response.Batch);
        var state = await NetworkSafetySnapshot.ReadCurrentStateAsync(fixture.Client, "network-guarded-incomplete");
        Assert.Single(state.State!.Subnets);
        Assert.DoesNotContain("delete_subnet", requests.Methods());
    }
    [Theory]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public async Task ConnectedDelete_IsInformationalInBothModes(McpAccessMode mode)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded", mode);
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Delete());
        var guard = Assert.Single(response.Guards);
        Assert.Equal("network_delete_connected_subnet", guard.Id);
        Assert.Equal("info", guard.Severity);
        Assert.Null(guard.Acknowledged);
        Assert.True(response.Success);
        Assert.Equal(2, response.Effects[0].Effect!.AffectedNodes.Count);
        Assert.Contains(response.Verification!.FinalChecks, c => c.Name.Contains("PLC_Ungrouped") && c.Status == "passed");
    }
    [Theory]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public async Task IncompleteInventory_BlocksBothModes(McpAccessMode mode)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-incomplete", mode);
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Delete());
        Assert.Equal("blocked", response.Phase);
        Assert.Null(response.Batch);
    }
    [Fact]
    public async Task Readonly_DeniesBeforeGate()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded", McpAccessMode.ReadOnly);
        var before = requests.Methods().Length;
        fixture.Binding.Clear(null, out _);
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Delete());
        Assert.Equal("access_denied", response.Error!.Category);
        Assert.Equal(before, requests.Methods().Length);
    }
    [Fact]
    public void NetworkAcknowledgeRegistration_IsRejected() => Assert.Throws<ArgumentException>(() =>
        NetworkGuardDefinitions.Validate(new[] { new WriteGuardDefinition("network_test", "acknowledge", "test") }));
    [Fact]
    public async Task BindingChange_PreventsDispatch()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        fixture.Binding.Clear(null, out _);
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Delete());
        Assert.Equal("binding_conflict", response.Error!.Category);
        Assert.Null(response.Batch);
    }

    [Fact]
    public async Task DomainValidation_RejectsMixedProjectsAndOversizedCallsBeforeWorker()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var before = requests.Methods().Length;
        var left = NetworkGuardedWriteFixture.Delete("left"); left.ProjectPath = "C:/one.ap21";
        var right = NetworkGuardedWriteFixture.Delete("right"); right.ProjectPath = "C:/two.ap21";
        Assert.Equal("validation_error", (await fixture.RunAsync(false, left, right)).Error!.Category);
        Assert.Equal("validation_error", (await fixture.RunAsync(false, Enumerable.Range(0, 51).Select(i => NetworkGuardedWriteFixture.Delete(i.ToString())).ToArray())).Error!.Category);
        Assert.Equal(before, requests.Methods().Length);
    }
    [Theory]
    [InlineData("device")]
    [InlineData("node")]
    [InlineData("subnet")]
    [InlineData("diagnostic")]
    public void PartialDiscoveryCannotEstablishUniqueness(string degraded)
    {
        var state = new HardwareConfigInfo { RootDeviceCount = 1, Devices = new() { new() { Name = "known" } }, Subnets = new() { new() { SubnetId = "known" } } };
        if (degraded == "device") state.Devices.Add(new());
        if (degraded == "subnet") state.Subnets.Add(new());
        if (degraded == "node") state.Devices[0].Items.Add(new() { NetworkInterfaces = new() { new() { Nodes = new() { new() } } } });
        if (degraded == "diagnostic") state.Messages.Add("Some device groups could not be enumerated.");
        Assert.False(NetworkWritePlanner.DiscoveryComplete(state));
    }
}
