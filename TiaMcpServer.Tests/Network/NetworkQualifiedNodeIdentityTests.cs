using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using Xunit;
namespace TiaMcpServer.Tests.Network;
[Collection(RealWorkerProcessCollection.Name)]
public sealed class NetworkQualifiedNodeIdentityTests
{
    internal static NetworkOperationRequest Configure(int position, string address, string id) => new()
    {
        OperationId = id, Operation = "configure_network_device", Target = new() { DeviceName = "S7-1500/ET200MP station_1", NodeId = "E1", InterfacePath = new NetworkInterfacePathSegment[] {
            new() { Name = "PLC_DP", PositionNumber = 1 }, new() { Name = position == 32768 ? "PROFINET interface_1" : "PROFINET interface_2", PositionNumber = position } } }, Changes = new() { IpAddress = address }
    };
    [Theory][InlineData(false)][InlineData(true)]
    public async Task BothE1Expectations_SurviveEitherConfigurationOrder(bool reverse)
    {
        using var audit = new TempAuditDirectory();
        using var f = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-qualified-read");
        var a = Configure(32768, "192.168.12.7", "x1"); var b = Configure(33024, "192.168.13.8", "x2");
        var result = await f.RunAsync(false, reverse ? b : a, reverse ? a : b);
        Assert.True(result.Success);
        Assert.Equal(2, result.Verification!.FinalChecks.Count(c => c.Name.EndsWith("/Address") && c.Status == "passed"));
        Assert.All(result.Effects, e => Assert.NotNull(Assert.Single(e.Effect!.AffectedNodes).InterfacePath));
    }
    [Fact]
    public async Task ConnectedDeletion_PreservesBothQualifiedNodes()
    {
        using var audit = new TempAuditDirectory(); using var f = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-qualified-delete");
        var baseline = await NetworkWritePlanner.ReadCurrentStateAsync(f.Client, "network-qualified-delete");
        var result = await f.RunAsync(false, NetworkGuardedWriteFixture.Delete());
        Assert.True(result.Success);
        Assert.Equal(2, result.Effects[0].Effect!.AffectedNodes.Count);
        Assert.All(result.Effects[0].Effect!.AffectedNodes, n => Assert.NotNull(n.InterfacePath));
        Assert.Equal(2, result.Verification!.FinalChecks.Count(c => c.Name.EndsWith("/removedSubnet:subnet-1") && c.Status == "passed"));
        var final = await NetworkWritePlanner.ReadCurrentStateAsync(f.Client, "network-qualified-delete");
        Assert.Equal(baseline.State!.RootDeviceCount, final.State!.RootDeviceCount);
    }
    [Fact]
    public async Task LegacyAffectedIdentity_RequiresUniqueFreshUpgrade()
    {
        using var audit = new TempAuditDirectory(); using var log = new FakeWorkerRequestLog(audit.Path);
        using var f = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-qualified-legacy");
        var result = await f.RunAsync(false, NetworkGuardedWriteFixture.Delete());
        Assert.Equal("blocked", result.Phase); Assert.DoesNotContain("delete_subnet", log.Methods());
    }
    [Theory][InlineData("subnet")][InlineData("node")][InlineData("device")][InlineData("owner")][InlineData("root")]
    public async Task LateUnreadableCandidate_CannotProveDeletion(string kind)
    {
        var scenario = "network-qualified-late-" + kind;
        using var audit = new TempAuditDirectory(); using var log = new FakeWorkerRequestLog(audit.Path);
        using var f = await NetworkGuardedWriteFixture.CreateAsync(audit, scenario);
        var result = await f.RunAsync(false, NetworkGuardedWriteFixture.Delete());
        Assert.Equal("applied", result.Phase); Assert.False(result.Success);
        Assert.Equal("succeeded", result.Batch!.Operations[0].Status);
        Assert.Contains(result.Verification!.FinalChecks, c => c.Status == "unverified");
        Assert.Single(log.Methods().Where(m => m == "delete_subnet"));
    }
    [Fact]
    public async Task LateUnreadableSubnet_CannotProveDeletionAbsence()
    {
        const string scenario = "network-guarded-late-unreadable-subnet";
        using var audit = new TempAuditDirectory(); using var log = new FakeWorkerRequestLog(audit.Path);
        using var f = await NetworkGuardedWriteFixture.CreateAsync(audit, scenario);
        var result = await f.RunAsync(false, NetworkGuardedWriteFixture.Delete());
        Assert.Equal("applied", result.Phase); Assert.False(result.Success);
        Assert.Contains(result.Verification!.FinalChecks, c => c.Name.EndsWith("/absent") && c.Status == "unverified");
        Assert.Single(log.Methods().Where(m => m == "delete_subnet"));
    }
    [Fact]
    public async Task FinalOptionalDiagnostic_DoesNotFailVerifiedWrite()
    {
        using var audit = new TempAuditDirectory(); using var f = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-qualified-read");
        var r = await f.RunAsync(false, Configure(32768, "192.168.12.7", "x1")); Assert.True(r.Success);
    }
}
