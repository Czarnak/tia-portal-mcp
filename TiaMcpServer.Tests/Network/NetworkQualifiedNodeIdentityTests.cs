using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;
namespace TiaMcpServer.Tests.Network;
[Collection(RealWorkerProcessCollection.Name)]
public sealed class NetworkQualifiedNodeIdentityTests
{

    private static NetworkNodeIdentityInfo Identity(string device, params (string Name, int Position)[] path) => new()
    { DeviceName = device, NodeId = "E1", InterfacePath = path.Select(s => new NetworkInterfacePathSegmentInfo { Name = s.Name, PositionNumber = s.Position }).ToList() };
    [Fact]
    public void SlashAndEscapedNames_DoNotCollide()
    {
        var a = Identity("PLC", ("rack/a", 1), ("b\\\"", 2));
        var b = Identity("PLC", ("rack", 1), ("a/b\\\"", 2));
        Assert.Equal(2, new HashSet<NetworkNodeIdentityInfo>(new[] { a, b }, NetworkNodeIdentityComparer.Instance).Count);
        var map = new Dictionary<NetworkNodeIdentityInfo, string>(NetworkNodeIdentityComparer.Instance) { [a] = "x1", [b] = "x2" };
        Assert.Equal(2, map.Count);
        Assert.Equal("x1", map[new() { DeviceName = "plc", NodeId = "E1", InterfacePath = NetworkInterfacePathEncoding.Decode(NetworkInterfacePathEncoding.Encode(a.InterfacePath!)).ToList() }]);
    }
    [Fact]
    public void DeviceCaseAndPathCase_FollowDeclaredRules()
    {
        var a = Identity("PLC", ("Rack", 1), ("Port", 2));
        var b = Identity("plc", ("Rack", 1), ("Port", 2));
        b.InterfaceName = "optional"; b.InterfacePath![1].TypeIdentifier = "optional";
        Assert.True(NetworkNodeIdentityComparer.Instance.Equals(a, b));
        Assert.Equal(NetworkNodeIdentityComparer.Instance.GetHashCode(a), NetworkNodeIdentityComparer.Instance.GetHashCode(b));
        Assert.False(NetworkNodeIdentityComparer.Instance.Equals(a, Identity("PLC", ("rack", 1), ("Port", 2))));
        Assert.False(NetworkNodeIdentityComparer.Instance.Equals(a, Identity("PLC", ("Rack", 1), ("Port", 3))));
        b.NodeId = "e1"; Assert.False(NetworkNodeIdentityComparer.Instance.Equals(a, b));
    }
    private sealed class Ancestor
    {
        public Ancestor? Parent; public string? Device; public NetworkInterfacePathSegmentInfo? Item;
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void OwnerHierarchy_MustResolveBackToActualNode(bool wrongNode)
    {
        var device = new Ancestor { Device = "PLC" }; var rack = new Ancestor { Parent = device, Item = new() { Name = "rack/\"", PositionNumber = 1 } };
        var owner = new Ancestor { Parent = rack, Item = new() { Name = "Interface", PositionNumber = 32768 } };
        var service = new Ancestor { Parent = owner }; var node = new Ancestor { Parent = service };
        NetworkNodeIdentityInfo Capture() => NetworkConnectionEvidenceCapture.CaptureOwner(node, "E1", v => v.Parent, v => v.Item, v => v.Device,
            identity => { Assert.Equal(new[] { "rack/\"", "Interface" }, identity.InterfacePath!.Select(p => p.Name)); return wrongNode ? new Ancestor { Parent = service } : node; });
        if (wrongNode) Assert.Throws<InvalidOperationException>(Capture); else Assert.Equal(32768, Capture().InterfacePath![1].PositionNumber);
    }
    [Fact]
    public void OwnerHierarchy_UnknownOrCyclicCannotCertify()
    {
        var node = new Ancestor(); var owner = new Ancestor { Item = new() { Name = "Interface", PositionNumber = 1 } }; node.Parent = owner; owner.Parent = node;
        Assert.Throws<InvalidOperationException>(() => NetworkConnectionEvidenceCapture.CaptureOwner(node, "E1", x => x.Parent, x => x.Item, x => x.Device, _ => node));
        owner.Parent = new Ancestor();
        Assert.Throws<InvalidOperationException>(() => NetworkConnectionEvidenceCapture.CaptureOwner(node, "E1", x => x.Parent, x => x.Item, x => x.Device, _ => node));
    }
    [Fact]
    public void QualifiedCapture_RetainsBothE1AndRejectsCanonicalDuplicates()
    {
        var a = Identity("PLC", ("Rack", 1), ("X1", 32768)); var b = Identity("PLC", ("Rack", 1), ("X2", 33024));
        var evidence = NetworkConnectionEvidenceCapture.CaptureSubnet(() => new[] { a, b }, n => n);
        Assert.True(evidence.Complete); Assert.Equal(2, new HashSet<NetworkNodeIdentityInfo>(evidence.Nodes, NetworkNodeIdentityComparer.Instance).Count);
        var duplicate = Identity("plc", ("Rack", 1), ("X1", 32768)); duplicate.InterfaceName = "optional";
        Assert.False(NetworkConnectionEvidenceCapture.CaptureSubnet(() => new[] { a, duplicate }, n => n).Complete);
    }
    [Fact]
    public async Task LegacyUniqueFreshUpgrade_RetainsDetachedOwner()
    {
        using var audit = new TempAuditDirectory(); using var f = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var preview = await f.RunAsync(true, NetworkGuardedWriteFixture.Delete());
        Assert.True(preview.Success); Assert.All(preview.Effects[0].Effect!.AffectedNodes, n => Assert.NotEmpty(n.InterfacePath!));
        var state = (await NetworkWritePlanner.ReadCurrentStateAsync(f.Client, "network-guarded")).State!;
        var identity = preview.Effects[0].Effect!.AffectedNodes[0];
        Assert.True(NetworkWritePlanner.TryResolveAffected(state, identity, out var detached, out _));
        identity.InterfacePath![0].Name = "Caller changed owner";
        Assert.NotEqual(identity.InterfacePath[0].Name, detached!.InterfacePath![0].Name);
        state.DiscoveryEvidence!.Complete = false;
        Assert.False(NetworkWritePlanner.TryResolveAffected(state, detached, out _, out _));
    }

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
        Assert.Equal(2, new HashSet<NetworkNodeIdentityInfo>(result.Effects[0].Effect!.AffectedNodes, NetworkNodeIdentityComparer.Instance).Count);
        Assert.Equal("network_delete_connected_subnet", Assert.Single(result.Guards).Id);
        Assert.All(result.Effects[0].Effect!.AffectedNodes, n => Assert.NotNull(n.InterfacePath));
        Assert.Equal(2, result.Verification!.FinalChecks.Count(c => c.Name.EndsWith("/removedSubnet:subnet-1") && c.Status == "passed"));
        var final = await NetworkWritePlanner.ReadCurrentStateAsync(f.Client, "network-qualified-delete");
        Assert.Equal(baseline.State!.RootDeviceCount, final.State!.RootDeviceCount);
        Assert.All(final.State.Devices[0].Items[0].Items.SelectMany(i => i.NetworkInterfaces).SelectMany(i => i.Nodes), n =>
        { Assert.True(n.ConnectionEvidence!.Complete); Assert.Null(n.ConnectionEvidence.SubnetId); Assert.Null(n.ConnectionEvidence.IoSystemSubnetId); Assert.Null(n.ConnectionEvidence.IoSystemNumber); });
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
        Assert.Single(log.Methods(), m => m == "delete_subnet");
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
        Assert.Single(log.Methods(), m => m == "delete_subnet");
    }
    [Fact]
    public async Task FinalOptionalDiagnostic_DoesNotFailVerifiedWrite()
    {
        using var audit = new TempAuditDirectory(); using var f = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-qualified-read");
        var r = await f.RunAsync(false, Configure(32768, "192.168.12.7", "x1")); Assert.True(r.Success);
    }
}
