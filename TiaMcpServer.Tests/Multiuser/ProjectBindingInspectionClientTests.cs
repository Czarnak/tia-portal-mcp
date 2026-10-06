using TiaMcpServer.Contracts;
using TiaMcpServer.Cursors;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Multiuser;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class ProjectBindingInspectionClientTests
{
    private const string Project = "C:/Projects/A.ap21";

    private sealed class Fixture : IDisposable
    {
        private readonly TempAuditDirectory directory = new();
        private readonly FakeWorkerPortals portals;
        private readonly FakeWorkerUiOpenProject ui;
        public Fixture(string? configured = null, bool open = true, TimeSpan? timeout = null, string? observation = null)
        {
            Directory.CreateDirectory(directory.Path);
            portals = new FakeWorkerPortals(new(42, open ? Project : null), new(43, observation is null ? null : $"C:/Projects/{observation}.ap21"));
            ui = new(open ? Project : null);
            Log = new(directory.Path);
            Binding = new(configured);
            Client = new(Binding, workerExecutablePath: FakeWorkerLocator.Locate(),
                requestTimeout: timeout ?? TimeSpan.FromSeconds(5), accessPolicy: new(McpAccessMode.ReadOnly));
        }
        public FakeWorkerRequestLog Log { get; }
        public ProjectSessionBinding Binding { get; }
        public OpennessWorkerClient Client { get; }
        public string[] Methods() => Log.Methods().Where(x => x != "hello").ToArray();
        public async Task Verify() => Assert.Null((await Client.BindOpenProjectAsync(Project, false)).Failure);
        public void Dispose() { Client.Dispose(); Log.Dispose(); ui.Dispose(); portals.Dispose(); directory.Dispose(); }
    }

    private static WorkerRequest Request(string alias = "Fixture", int? pid = null) => new()
    { Method = "list_server_groups", MultiuserServerAlias = alias, PortalProcessId = pid };

    [Theory]
    [InlineData("discovery-project-closed", null, "binding_conflict")]
    [InlineData("discovery-portal-lost", null, "binding_conflict")]
    [InlineData("discovery-missing-identity", null, "postcondition_failed")]
    [InlineData("discovery-project-closed", "target_not_found", "binding_conflict")]
    [InlineData("discovery-portal-lost", "target_ambiguous", "binding_conflict")]
    [InlineData("discovery-missing-identity", "target_not_found", "postcondition_failed")]
    public async Task DiscoveryIdentityLoss_InvalidatesWithoutSelectionOrReplay(string observation, string? selectorFailure, string category)
    {
        using var f = new Fixture(observation: observation); await f.Verify();
        var before = f.Client.BindingSnapshot;
        var methods = f.Methods().Length;
        var outcome = await f.Client.InspectPortalAsync(selectorFailure is null
            ? new WorkerRequest { Method = "list_tia_portal_processes" } : Request(selectorFailure));
        if (observation == "discovery-project-closed")
            Assert.Null(Assert.Single(outcome.Portals, portal => portal.ProcessId == 42).ProjectPath);
        if (observation == "discovery-portal-lost")
            Assert.DoesNotContain(outcome.Portals, portal => portal.ProcessId == 42);
        if (selectorFailure is null && observation != "discovery-missing-identity")
        {
            Assert.NotNull(outcome.Result.SessionIdentity);
            Assert.Null(outcome.Result.SessionIdentity.ProjectPath);
        }
        Assert.Equal(category, outcome.Result.FailureCategory);
        Assert.True(before.SameBinding(outcome.Before));
        Assert.Equal(ProjectBindingSnapshot.InvalidatedState, outcome.After.State);
        Assert.False(before.SameBinding(outcome.After));
        Assert.Equal(selectorFailure is null ? new[] { "list_tia_portal_processes" }
            : new[] { "list_server_groups", "list_tia_portal_processes" }, f.Methods().Skip(methods));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("target_not_found")]
    [InlineData("target_ambiguous")]
    public async Task HealthyDiscovery_PreservesVerifiedBindingAndOriginalFailure(string? selectorFailure)
    {
        using var f = new Fixture(); await f.Verify(); var before = f.Client.BindingSnapshot;
        var outcome = await f.Client.InspectPortalAsync(selectorFailure is null
            ? new WorkerRequest { Method = "list_tia_portal_processes" } : Request(selectorFailure));
        Assert.Equal(selectorFailure, outcome.Result.FailureCategory);
        Assert.Equal(selectorFailure is null, outcome.Result.Success);
        Assert.True(before.SameBinding(outcome.After));
        Assert.Equal(2, outcome.Portals.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Project)]
    public async Task Discovery_DoesNotPromoteUnverifiedBindingFromStampedIdentity(string? configured)
    {
        using var f = new Fixture(configured); var before = f.Client.BindingSnapshot;
        var outcome = await f.Client.InspectPortalAsync(new WorkerRequest { Method = "list_tia_portal_processes" });
        Assert.True(outcome.Result.Success, outcome.Result.Error);
        Assert.NotNull(outcome.Result.SessionIdentity);
        Assert.True(before.SameBinding(outcome.After));
        Assert.Equal(new[] { "list_tia_portal_processes" }, f.Methods());
    }

    [Fact]
    public async Task Verified_ForeignPidIsLocalNotSent_LeavesRevisionIntact()
    {
        using var f = new Fixture(); await f.Verify(); var before = f.Client.BindingSnapshot; var methods = f.Methods();
        var outcome = await f.Client.InspectPortalAsync(Request(pid: 43));
        Assert.Equal(WorkerFailureCategories.BindingConflict, outcome.Result.FailureCategory);
        Assert.Equal(WorkerDispatchState.NotSent, outcome.Result.DispatchState);
        Assert.True(before.SameBinding(outcome.Before)); Assert.True(before.SameBinding(outcome.After));
        Assert.Equal(methods, f.Methods());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(42)]
    public async Task Verified_ForwardsIdentityAndPidWithoutStatusOrProjectPath(int? pid)
    {
        using var f = new Fixture(); await f.Verify(); var before = f.Client.BindingSnapshot;
        var outcome = await f.Client.InspectPortalAsync(Request("expect-bound", pid));
        Assert.True(outcome.Result.Success, outcome.Result.Error);
        Assert.Equal(42, outcome.Result.PortalProcessId);
        Assert.True(before.SameBinding(outcome.After));
        Assert.Equal(new[] { "select_portal_project", "get_project_status", "list_server_groups" }, f.Methods());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(Project, false)]
    [InlineData(Project, true)]
    public async Task Unverified_DoesNotGroundConfiguredPathOrPromote(string? configured, bool invalidated)
    {
        using var f = new Fixture(configured, open: false);
        if (invalidated) f.Binding.Invalidate("old binding");
        var before = f.Client.BindingSnapshot;
        var outcome = await f.Client.InspectPortalAsync(Request("expect-unbound", 42));
        Assert.True(outcome.Result.Success, outcome.Result.Error);
        Assert.Equal(42, outcome.Result.PortalProcessId); Assert.True(before.SameBinding(outcome.After));
        Assert.Equal(new[] { "list_server_groups" }, f.Methods());
        var again = await f.Client.InspectPortalAsync(Request("expect-unbound"));
        Assert.True(again.Result.Success, again.Result.Error); Assert.True(before.SameBinding(again.After));
    }

    [Fact]
    public async Task OrdinaryServerFailure_PreservesHealthyBinding()
    {
        using var f = new Fixture(); await f.Verify(); var before = f.Client.BindingSnapshot;
        var outcome = await f.Client.InspectPortalAsync(Request("server-failure"));
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, outcome.Result.FailureCategory);
        Assert.True(before.SameBinding(outcome.After)); Assert.Equal(1, f.Methods().Count(x => x == "list_server_groups"));
    }

    [Theory]
    [InlineData("identity-loss", "binding_conflict")]
    [InlineData("hang", "worker_timeout")]
    [InlineData("crash", "worker_crashed")]
    public async Task GenuineLoss_InvalidatesWithoutReplay(string alias, string category)
    {
        using var f = new Fixture(timeout: TimeSpan.FromSeconds(2)); await f.Verify();
        var outcome = await f.Client.InspectPortalAsync(Request(alias));
        Assert.Equal(category, outcome.Result.FailureCategory);
        Assert.Equal(ProjectBindingSnapshot.InvalidatedState, outcome.After.State);
        Assert.Equal(1, f.Methods().Count(x => x == "list_server_groups"));
    }

    [Fact]
    public async Task StaleExpectedIdentity_InvalidatesWithoutReplay()
    {
        using var f = new Fixture(); await f.Verify();
        var identity = f.Client.BindingSnapshot.ToWorkerIdentity()!;
        identity.WorkerSessionId = "stale";
        Assert.True(f.Binding.BindVerified(identity, true, out _));
        var outcome = await f.Client.InspectPortalAsync(Request());
        Assert.Equal(WorkerFailureCategories.BindingConflict, outcome.Result.FailureCategory);
        Assert.Equal(ProjectBindingSnapshot.InvalidatedState, outcome.After.State);
        Assert.Equal(1, f.Methods().Count(x => x == "list_server_groups"));
    }

    [Theory]
    [InlineData("target_not_found")]
    [InlineData("target_ambiguous")]
    public async Task SelectorFailure_ListsCandidatesOnceWithoutReplaying(string alias)
    {
        using var f = new Fixture();
        var outcome = await f.Client.InspectPortalAsync(Request(alias));
        Assert.Equal(alias, outcome.Result.FailureCategory); Assert.Equal(2, outcome.Portals.Count);
        Assert.Equal(new[] { "list_server_groups", "list_tia_portal_processes" }, f.Methods());
    }

    [Fact]
    public async Task InspectionEntry_RefusesWriteMethodBeforeDispatch()
    {
        using var f = new Fixture();
        var outcome = await f.Client.InspectPortalAsync(new WorkerRequest { Method = "save_project" });
        Assert.False(outcome.Result.Success); Assert.Equal(WorkerDispatchState.NotSent, outcome.Result.DispatchState);
        Assert.Empty(f.Methods());
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    public async Task ExistingProjectTreeCursor_TracksInspectionBinding(bool foreignPid, bool discovery, bool loss)
    {
        const string scenario = "project-tree-v3-small";
        var path = Path.Combine(Path.GetDirectoryName(FakeWorkerLocator.Locate())!, scenario);
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, path),
            new FakeWorkerPortals.Entry(43, loss ? "C:/Projects/discovery-project-closed.ap21" : null));
        using var ui = FakeWorkerUiOpenProject.ForWorkerRelativePath(scenario);
        var binding = new ProjectSessionBinding(path);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: FakeWorkerLocator.Locate(), accessPolicy: new(McpAccessMode.ReadOnly));
        var codec = new ProjectTreeCursorCodec(new AuthenticatedCursorProtector(Enumerable.Range(0, 32).Select(x => (byte)x).ToArray(), "inspection-test"));
        using var store = new ProjectTreeSnapshotStore();
        var coordinator = new ProjectTreeBrowseCoordinator(client, codec, store, new ProjectTreePageProjector(codec), TimeProvider.System);
        var first = await coordinator.BrowseAsync(new ProjectTreeBrowseRequest(PageSize: 1));
        Assert.True(first.IsSuccess, first.CanonicalText);
        var cursor = first.Response.Result!.Pagination.NextCursor;
        Assert.NotNull(cursor);
        var inspection = await client.InspectPortalAsync(discovery ? new WorkerRequest { Method = "list_tia_portal_processes" }
            : Request(loss ? "target_not_found" : "expect-bound", foreignPid ? 43 : 42));
        Assert.Equal(!foreignPid && !loss, inspection.Result.Success);
        var next = await coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: cursor));
        Assert.Equal(!loss, next.IsSuccess);
        if (loss) Assert.Equal(WorkerFailureCategories.CursorBindingMismatch, next.Response.Failure!.Category);
        else Assert.Equal(new[] { 1, 2, 3 }, next.Response.Result!.Nodes.Select(node => node.Sequence));
    }

    [Fact]
    public async Task Configured_AttachedForeignPidRefusalDoesNotInvalidateAssertion()
    {
        using var f = new Fixture(Project, open: false);
        Assert.True((await f.Client.InspectPortalAsync(Request("Fixture", 42))).Result.Success);
        var before = f.Client.BindingSnapshot;
        var refused = await f.Client.InspectPortalAsync(Request("Fixture", 43));
        Assert.Equal("binding_conflict", refused.Result.FailureCategory);
        Assert.True(before.SameBinding(refused.After));
        Assert.True((await f.Client.InspectPortalAsync(Request("Fixture", 42))).Result.Success);
    }
}
