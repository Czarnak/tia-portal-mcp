using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Network;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class HardwareDiscoveryEvidenceTests
{
    public static IEnumerable<object[]> StructuralStages() => new[]
    {
        "deviceEnumeration", "deviceMaterialization", "deviceItemEnumeration", "deviceItemMaterialization",
        "interfaceDiscovery", "nodeEnumeration", "nodeMaterialization", "subnetEnumeration", "subnetMaterialization",
        "ioSystemEnumeration", "ioSystemMaterialization", "deviceSelection"
    }.Select(stage => new object[] { stage });

    [Fact]
    public void OptionalMetadata_DoesNotMakeTraversalIncomplete()
    {
        var optionalMetadataState = NetworkDiscoveryRepairFixture.Metadata(new() { Scope = "project", Complete = true });
        Assert.NotEmpty(optionalMetadataState.Messages);
        Assert.True(optionalMetadataState.DiscoveryEvidence!.Complete);
        Assert.True(NetworkWritePlanner.DiscoveryComplete(optionalMetadataState));
    }

    [Fact]
    public void EnumerationThrowsDuringMoveNext_IsIncomplete()
    {
        var diagnostics = new List<string>();
        var throwingCapture = new HardwareDiscoveryEvidenceCapture("project", diagnostics.Add);
        var seen = new List<int>();
        throwingCapture.Traverse(ThrowAfterFirst, seen.Add, "nodeEnumeration", "nodeMaterialization");
        Assert.False(throwingCapture.Evidence.Complete);
        Assert.Contains(throwingCapture.Evidence.Failures, f => f.Stage == "nodeEnumeration");
        Assert.Equal(new[] { 1 }, seen);
        Assert.Contains(diagnostics, message => message.Contains("MoveNext unavailable"));
    }

    [Theory]
    [MemberData(nameof(StructuralStages))]
    public void CollectionAcquisitionFailure_IsIncomplete(string stage)
    {
        var diagnostics = new List<string>();
        var capture = new HardwareDiscoveryEvidenceCapture(stage == "deviceSelection" ? "device" : "project", diagnostics.Add);
        capture.Traverse<int>(() => throw new InvalidOperationException("collection unavailable"), _ => Assert.Fail("must not materialize"), stage, stage);
        Assert.False(capture.Evidence.Complete);
        Assert.Equal(stage, Assert.Single(capture.Evidence.Failures).Stage);
        Assert.Contains(diagnostics, message => message.Contains("collection unavailable"));
    }

    [Theory]
    [InlineData("deviceMaterialization")]
    [InlineData("deviceItemMaterialization")]
    [InlineData("nodeMaterialization")]
    [InlineData("subnetMaterialization")]
    [InlineData("ioSystemMaterialization")]
    public void SkippedMaterialization_PreservesRemainingCandidates(string stage)
    {
        var capture = new HardwareDiscoveryEvidenceCapture("project", _ => { });
        var seen = new List<int>();
        capture.Traverse(() => new[] { 1, 2, 3 }, value => { if (value == 2) throw new InvalidOperationException("materialization unavailable"); seen.Add(value); }, "deviceEnumeration", stage);
        Assert.False(capture.Evidence.Complete);
        Assert.Equal(new[] { 1, 3 }, seen);
        Assert.Equal(stage, Assert.Single(capture.Evidence.Failures).Stage);
    }

    [Fact]
    public void ServiceException_IsIncompleteButKnownNullServiceIsComplete()
    {
        var absent = new HardwareDiscoveryEvidenceCapture("project", _ => { });
        absent.Traverse(() => Array.Empty<object>(), _ => Assert.Fail("known absence"), "interfaceDiscovery", "interfaceDiscovery");
        Assert.True(absent.Evidence.Complete);
        var failed = new HardwareDiscoveryEvidenceCapture("project", _ => { });
        failed.Traverse<object>(() => throw new InvalidOperationException("service unavailable"), _ => { }, "interfaceDiscovery", "interfaceDiscovery");
        Assert.False(failed.Evidence.Complete);
        Assert.Equal("interfaceDiscovery", Assert.Single(failed.Evidence.Failures).Stage);
    }

    [Theory]
    [InlineData("device", false)]
    [InlineData("project", true)]
    [InlineData(null, false)]
    public void FilteredOrPagedRead_CannotProveProjectComplete(string? scope, bool paged)
    {
        var state = new HardwareConfigInfo { DiscoveryEvidence = scope is null ? null : new() { Scope = scope, Complete = true },
            Pagination = paged ? new(0, 0, 0, 0, null) : null };
        Assert.False(NetworkWritePlanner.DiscoveryComplete(state));
    }

    [Fact]
    public void MissingRootCount_IsIndependentOfTraversal()
    {
        var state = NetworkDiscoveryRepairFixture.Metadata(new() { Scope = "project", Complete = true });
        state.RootDeviceCount = null;
        Assert.True(NetworkWritePlanner.DiscoveryComplete(state));
        Assert.Null(state.RootDeviceCount);
    }

    [Theory]
    [MemberData(nameof(StructuralStages))]
    public async Task TraversalFailure_BlocksEveryWrite(string stage)
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-traversal-" + stage);
        foreach (var operation in new[]
        {
            NetworkGuardedWriteFixture.Configure("configure", "10.0.0.1"), NetworkGuardedWriteFixture.Delete(),
            new NetworkOperationRequest { OperationId = "add", Operation = "add_network_device", DeviceName = "new", TypeIdentifier = "OrderNumber:TEST" },
            new NetworkOperationRequest { OperationId = "create", Operation = "create_subnet", Subnet = new() { Name = "new", NetworkType = "Ethernet" } },
            new NetworkOperationRequest { OperationId = "update", Operation = "update_subnet", Target = new() { Kind = "subnet", SubnetId = "subnet-1" }, SubnetChanges = new() { Name = "new" } }
        })
        {
            var response = await fixture.RunAsync(false, operation);
            Assert.Equal("error", response.Phase);
            Assert.Equal("worker_operation_failed", response.Error!.Category);
            Assert.Null(response.Batch);
        }
        Assert.DoesNotContain(requests.Methods(), method => method is "add_network_device" or "configure_network_device" or "create_subnet" or "update_subnet" or "delete_subnet");
    }

    [Fact]
    public async Task ReadableTarget_WithUnreadableCompetingIdentity_RefusesBeforeDispatch()
    {
        foreach (var kind in new[] { "device", "node", "subnet", "io", "subnet-name" })
        {
            using var audit = new TempAuditDirectory();
            using var requests = new FakeWorkerRequestLog(audit.Path);
            using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-identity-" + kind);
            var operation = NetworkGuardedWriteFixture.Configure("configure", "10.0.0.1");
            if (kind == "subnet") operation = NetworkGuardedWriteFixture.Delete();
            if (kind == "io") operation.Changes = new() { IoSystem = new() { SubnetId = "subnet-1", Number = 1 } };
            if (kind == "subnet-name") operation = new() { OperationId = "create", Operation = "create_subnet", Subnet = new() { Name = "new", NetworkType = "Ethernet" } };
            var response = await fixture.RunAsync(false, operation);
            Assert.Equal("error", response.Phase);
            Assert.Equal("worker_operation_failed", response.Error!.Category);
            Assert.Null(response.Batch);
            Assert.DoesNotContain(requests.Methods(), method => method is "configure_network_device" or "delete_subnet" or "create_subnet");
        }
    }

    [Fact]
    public async Task OptionalMetadata_PreparesAndVerifiesWithoutLosingDiagnostics()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-optional-metadata");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("configure", "10.0.0.1"));
        Assert.True(response.Success);
        Assert.True(response.Verification!.Success);
        var read = await NetworkWritePlanner.ReadCurrentStateAsync(fixture.Client, "network-guarded-optional-metadata");
        Assert.NotEmpty(read.State!.Messages);
        Assert.NotEmpty(read.State.Devices[0].Items[0].SelectorDiagnostics);
    }

    [Fact]
    public async Task MissingEvidence_ReadsRemainCompatibleButWritesRefuse()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-missing-discovery");
        var read = await NetworkWritePlanner.ReadCurrentStateAsync(fixture.Client, "network-guarded-missing-discovery");
        Assert.True(read.Success);
        Assert.Null(read.State!.DiscoveryEvidence);
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("configure", "10.0.0.1"));
        Assert.Equal("error", response.Phase);
        Assert.DoesNotContain("configure_network_device", requests.Methods());
    }

    [Fact]
    public async Task LateTraversalFailure_PreservesEarlierOutcome()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-late-traversal");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("first", "10.0.0.1"), NetworkGuardedWriteFixture.Delete(), NetworkGuardedWriteFixture.Configure("last", "10.0.0.3"));
        Assert.Equal("applied", response.Phase);
        Assert.False(response.Success);
        Assert.Null(response.Error);
        Assert.Equal("succeeded", response.Batch!.Operations[0].Status);
        Assert.Equal("failed", response.Batch.Operations[1].Status);
        Assert.Equal("earlierOperationFailed", response.Batch.Operations[2].SkipReason);
        Assert.Contains(response.Verification!.FinalChecks, check => check.Name == "finalHardwareState" && check.Status == "unverified");
        Assert.Single(requests.Methods(), method => method == "configure_network_device");
        Assert.DoesNotContain("delete_subnet", requests.Methods());
    }

    private static IEnumerable<int> ThrowAfterFirst()
    {
        yield return 1;
        throw new InvalidOperationException("MoveNext unavailable");
    }
}
