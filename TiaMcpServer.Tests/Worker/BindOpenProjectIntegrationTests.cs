using System.Text.Json;
using TiaMcpServer.Network;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class BindOpenProjectIntegrationTests
{
    private const string A = "C:/Projects/A.ap21";
    private const string B = "C:/Projects/B.ap21";

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "binding-tests-" + Guid.NewGuid().ToString("N"));
        private readonly FakeWorkerPortals portals;
        private readonly FakeWorkerUiOpenProject ui;
        public Fixture(string? configured = null, string? initial = A, McpAccessMode mode = McpAccessMode.ReadOnly,
            TimeSpan? timeout = null, params FakeWorkerPortals.Entry[] entries)
        {
            Directory.CreateDirectory(directory);
            portals = new FakeWorkerPortals(entries);
            ui = new FakeWorkerUiOpenProject(initial);
            Log = new FakeWorkerRequestLog(directory);
            Binding = new ProjectSessionBinding(configured);
            Client = new OpennessWorkerClient(Binding, workerExecutablePath: FakeWorkerLocator.Locate(),
                requestTimeout: timeout ?? TimeSpan.FromSeconds(5), accessPolicy: new OperationAccessPolicy(mode));
        }
        public FakeWorkerRequestLog Log { get; }
        public string[] Methods() => Log.Methods().Where(m => m != "hello").ToArray();
        public bool SelectionDispatched()
        {
            var path = Directory.GetFiles(directory, "requests-*.log").SingleOrDefault();
            if (path is null) return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Split('\n').Any(line => line.Trim() == "select_portal_project");
        }
        public ProjectSessionBinding Binding { get; }
        public OpennessWorkerClient Client { get; }
        public async Task Verify()
        {
            var status = await Client.GetProjectStatusAsync(A);
            Assert.True(status.Success, status.Error);
            Assert.True(Binding.BindVerified(status.SessionIdentity, false, out var error), error);
        }
        public void Dispose()
        {
            Client.Dispose(); Log.Dispose(); ui.Dispose(); portals.Dispose(); Directory.Delete(directory, true);
        }
    }

    private static Fixture Two(string? configured = null, bool headless = false, bool modified = false, string target = B, TimeSpan? timeout = null)
        => new(configured, A, timeout: timeout, entries: new[] { new FakeWorkerPortals.Entry(42, A, !headless, Modified: modified), new FakeWorkerPortals.Entry(43, target) });
    private static void Success(ProjectBindingOutcome result, string transition)
    {
        Assert.Null(result.Failure); Assert.False(result.IsRejection); Assert.Equal(transition, result.Transition);
        Assert.True(result.After.IsVerified); Assert.True(result.Project?.IsOpen); Assert.Null(result.Project!.Metadata);
    }
    private static void Failure(ProjectBindingOutcome result, string category, string state, bool rejection = false)
    {
        Assert.Equal(category, result.Failure?.FailureCategory); Assert.Equal(state, result.After.State);
        Assert.Equal(ProjectBindingTransitions.None, result.Transition); Assert.Equal(rejection, result.IsRejection);
    }

    [Fact]
    public async Task Unbound_SingleProject_Binds()
    {
        using var f = new Fixture(entries: new[] { new FakeWorkerPortals.Entry(42, A) });
        var result = await f.Client.BindOpenProjectAsync(null, false);
        Success(result, ProjectBindingTransitions.Bound); Assert.Single(result.Portals);
        Assert.Equal(new[] { "list_tia_portal_processes", "select_portal_project", "get_project_status" }, f.Methods());
    }
    [Fact]
    public async Task Unbound_SeveralPortals_AmbiguousWithPortals()
    {
        using var f = Two(); var result = await f.Client.BindOpenProjectAsync(null, false);
        Failure(result, WorkerFailureCategories.TargetAmbiguous, ProjectBindingSnapshot.UnboundState);
        Assert.Equal(2, result.Portals.Count); Assert.Equal(new[] { "list_tia_portal_processes" }, f.Methods());
    }
    [Fact]
    public async Task Unbound_NoProjects_TargetNotFound()
    {
        using var f = new Fixture(initial: null); var result = await f.Client.BindOpenProjectAsync(null, false);
        Failure(result, WorkerFailureCategories.TargetNotFound, ProjectBindingSnapshot.UnboundState); Assert.Empty(result.Portals);
    }
    [Fact]
    public async Task Unbound_WithPath_SelectsWithoutListing()
    {
        using var f = Two(); var result = await f.Client.BindOpenProjectAsync(B, false);
        Success(result, ProjectBindingTransitions.Bound); Assert.Empty(result.Portals);
        Assert.Equal(new[] { "select_portal_project", "get_project_status" }, f.Methods());
    }
    [Fact]
    public async Task BindWithPath_NotFound_ListsPortals()
    {
        using var f = Two(); var result = await f.Client.BindOpenProjectAsync("C:/Projects/Missing.ap21", false);
        Failure(result, WorkerFailureCategories.TargetNotFound, ProjectBindingSnapshot.UnboundState); Assert.Equal(2, result.Portals.Count);
        Assert.Equal(new[] { "select_portal_project", "list_tia_portal_processes" }, f.Methods());
    }
    [Theory]
    [InlineData(null)]
    [InlineData(A)]
    public async Task Configured_NoPathOrSamePath_SelectsAndAdopts(string? path)
    {
        using var f = Two(A); Success(await f.Client.BindOpenProjectAsync(path, false), ProjectBindingTransitions.Bound);
        Assert.Equal(new[] { "select_portal_project", "get_project_status" }, f.Methods());
    }
    [Theory]
    [InlineData(ProjectBindingSnapshot.ConfiguredUnverifiedState)]
    [InlineData(ProjectBindingSnapshot.InvalidatedState)]
    [InlineData(ProjectBindingSnapshot.VerifiedState)]
    public async Task DifferentPathNoForce_RejectedNothingSent(string state)
    {
        using var f = Two(A);
        if (state != ProjectBindingSnapshot.ConfiguredUnverifiedState) await f.Verify();
        if (state == ProjectBindingSnapshot.InvalidatedState) f.Binding.Invalidate("test");
        var before = f.Binding.CaptureSnapshot(); var count = f.Methods().Length;
        var result = await f.Client.BindOpenProjectAsync(B, false);
        Failure(result, WorkerFailureCategories.BindingConflict, state, true);
        Assert.True(before.SameBinding(result.After)); Assert.Equal(count, f.Methods().Length);
    }
    [Fact]
    public async Task Configured_OtherPathForce_Binds()
    {
        using var f = Two(A); Success(await f.Client.BindOpenProjectAsync(B, true), ProjectBindingTransitions.Bound);
    }
    [Fact]
    public async Task Invalidated_NoPath_ReselectsRetainedPath()
    {
        using var f = Two(A); await f.Verify(); f.Binding.Invalidate("test");
        Success(await f.Client.BindOpenProjectAsync(null, false), ProjectBindingTransitions.Bound);
        Assert.Equal("select_portal_project", f.Methods()[1]);
    }
    [Fact]
    public async Task Verified_SamePath_UnchangedRevision()
    {
        using var f = Two(); await f.Verify(); var before = f.Binding.CaptureSnapshot();
        var result = await f.Client.BindOpenProjectAsync(A.ToLowerInvariant(), true);
        Success(result, ProjectBindingTransitions.Unchanged); Assert.True(before.SameBinding(result.After)); Assert.Empty(result.Portals);
        Assert.Equal(new[] { "get_project_status", "get_project_status" }, f.Methods());
    }
    [Fact]
    public async Task Verified_NoPath_Reverifies_MismatchInvalidates()
    {
        using var f = Two(); await f.Verify();
        var before = f.Binding.CaptureSnapshot();
        Assert.True(f.Binding.BindVerified(new WorkerSessionIdentity { WorkerSessionId = "stale", PortalProcessId = 42, SessionGeneration = before.SessionGeneration!.Value, ProjectPath = A }, true, out _));
        Failure(await f.Client.BindOpenProjectAsync(null, false), WorkerFailureCategories.BindingConflict, ProjectBindingSnapshot.InvalidatedState);
    }
    [Fact]
    public async Task Verified_OtherPathForce_Switches()
    {
        using var f = Two(); await f.Verify(); var result = await f.Client.BindOpenProjectAsync(B, true);
        Success(result, ProjectBindingTransitions.Switched); Assert.True(result.After.Revision > result.Before.Revision);
        Assert.Equal(43, result.After.PortalProcessId);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task Verified_UnknownPreviousModifiedState_SwitchesAndVerifies(bool ui, int otherClients)
    {
        using var f = new Fixture(mode: McpAccessMode.ReadOnly, entries: new[]
        {
            new FakeWorkerPortals.Entry(42, A, ui, otherClients, Modified: null),
            new FakeWorkerPortals.Entry(43, B)
        });
        await f.Verify();
        var result = await f.Client.BindOpenProjectAsync(B, true);
        Success(result, ProjectBindingTransitions.Switched);
        Assert.Equal(43, result.After.PortalProcessId);
        Assert.Equal(ProjectPathNormalization.Canonicalize(B), result.After.ProjectPath);
        Assert.Equal(new[] { "get_project_status", "select_portal_project", "get_project_status" }, f.Methods());
    }

    [Fact]
    public async Task Verified_UnknownPreviousModifiedState_HeadlessSoleClientRefusesBeforeDetach()
    {
        using var f = new Fixture(mode: McpAccessMode.ReadOnly, entries: new[]
        {
            new FakeWorkerPortals.Entry(42, A, HasUserInterface: false, Modified: null),
            new FakeWorkerPortals.Entry(43, B)
        });
        await f.Verify();
        var before = f.Binding.CaptureSnapshot();
        var result = await f.Client.BindOpenProjectAsync(B, true);
        Failure(result, WorkerFailureCategories.BindingConflict, ProjectBindingSnapshot.VerifiedState, true);
        Assert.True(before.SameBinding(result.After));
        Assert.Equal(new[] { "get_project_status", "select_portal_project" }, f.Methods());
    }
    [Fact]
    public async Task Switch_TargetNotAdvertised_KeepsPrevious()
    {
        using var f = Two(); await f.Verify(); var result = await f.Client.BindOpenProjectAsync("C:/Missing.ap21", true);
        Failure(result, WorkerFailureCategories.TargetNotFound, ProjectBindingSnapshot.VerifiedState);
        Assert.True(result.Before.SameBinding(result.After)); Assert.Equal(2, result.Portals.Count);
    }
    [Fact]
    public async Task HeadlessRefusal_KeepsPrevious()
    {
        using var f = Two(headless: true, modified: true); await f.Verify(); var result = await f.Client.BindOpenProjectAsync(B, true);
        Failure(result, WorkerFailureCategories.BindingConflict, ProjectBindingSnapshot.VerifiedState, true);
        Assert.True(result.Before.SameBinding(result.After)); Assert.Empty(result.Portals);
        Assert.Equal(new[] { "get_project_status", "select_portal_project" }, f.Methods());
    }
    [Theory]
    [InlineData("portal-switch-fails-after-detach", WorkerFailureCategories.WorkerOperationFailed)]
    [InlineData("portal-switch-crash", WorkerFailureCategories.WorkerCrashed)]
    [InlineData("portal-switch-hang", WorkerFailureCategories.WorkerTimeout)]
    [InlineData("portal-selection-malformed", WorkerFailureCategories.ProtocolError)]
    public async Task SwitchFailure_InvalidatesPrevious(string scenario, string category)
    {
        using var f = Two(target: "C:/Projects/" + scenario + ".ap21", timeout: TimeSpan.FromMilliseconds(700));
        await f.Verify(); var result = await f.Client.BindOpenProjectAsync("C:/Projects/" + scenario + ".ap21", true);
        Failure(result, category, ProjectBindingSnapshot.InvalidatedState);
        Assert.DoesNotContain("untrustedMarker", result.Failure!.Error!);
        if (category == WorkerFailureCategories.WorkerTimeout) Assert.Contains("Openness dialog", result.Failure.Error!);
    }
    [Fact]
    public async Task Switch_WorkerRestartedSinceVerify_IdentityMismatchInvalidates()
    {
        using var f = Two(); await f.Verify(); f.Client.Dispose();
        Failure(await f.Client.BindOpenProjectAsync(B, true), WorkerFailureCategories.BindingConflict, ProjectBindingSnapshot.InvalidatedState);
    }
    [Fact]
    public async Task Switch_Reattached_WarnsAboutOpennessDialog()
    {
        using var f = Two(); await f.Verify(); var result = await f.Client.BindOpenProjectAsync(B, true);
        Success(result, ProjectBindingTransitions.Switched); Assert.Contains(result.Warnings, w => w.Contains("Openness") && w.Contains("Yes to all"));
    }
    [Fact]
    public async Task SelectionIdentityForOtherPath_PostconditionFailed()
    {
        const string target = "C:/Projects/portal-selection-other-path.ap21";
        using var f = Two(target: target); await f.Verify();
        var result = await f.Client.BindOpenProjectAsync(target, true);
        Failure(result, WorkerFailureCategories.PostconditionFailed, ProjectBindingSnapshot.InvalidatedState);
        Assert.Equal(result.Before.ProjectPath, result.After.ProjectPath);
        Assert.Equal(new[] { "get_project_status", "select_portal_project" }, f.Methods());
    }
    [Fact]
    public async Task Switch_PreviousWorkerOpenedModified_Warns()
    {
        const string source = "C:/Projects/guarded-lifecycle-modified.ap21";
        using var f = new Fixture(initial: null, mode: McpAccessMode.ReadWrite,
            entries: new[] { new FakeWorkerPortals.Entry(42, null, Modified: true), new FakeWorkerPortals.Entry(43, B) });
        var opened = await f.Client.OpenProjectAsync(source, false); Assert.True(opened.Success, opened.Error);
        var result = await f.Client.BindOpenProjectAsync(B, true);
        Success(result, ProjectBindingTransitions.Switched);
        Assert.Contains(result.Warnings, w => w.Contains("unsaved changes") && w.Contains("remains open"));
    }
    [Theory]
    [InlineData("")]
    [InlineData("relative.ap21")]
    [InlineData("C:/Projects/A.txt")]
    public async Task InvalidPath_RejectsBeforeWorkerActivity(string path)
    {
        using var f = Two(); Failure(await f.Client.BindOpenProjectAsync(path, false), WorkerFailureCategories.ValidationError, ProjectBindingSnapshot.UnboundState, true);
        Assert.Empty(f.Log.Methods());
    }
    private static NetworkOperationRequest[] AddDevice(string path) => new[]
    {
        new NetworkOperationRequest { OperationId = "add", Operation = "add_network_device", ProjectPath = path,
            TypeIdentifier = "OrderNumber:6ES7 510-1DJ01-0AB0/V2.0", DeviceName = "PLC_1" }
    };
    [Fact]
    public async Task NetworkWriteToken_PreviewedBeforeForceSwitch_RejectedAfterSwitch()
    {
        const string source = "C:/Projects/network-roundtrip.ap21";
        using var f = new Fixture(initial: source, mode: McpAccessMode.ReadWrite,
            entries: new[] { new FakeWorkerPortals.Entry(42, source), new FakeWorkerPortals.Entry(43, B) });
        using var audit = new TempAuditDirectory(); var safety = audit.CreateSafety(projectSessionBinding: f.Binding);
        Success(await f.Client.BindOpenProjectAsync(source, false), ProjectBindingTransitions.Bound);
        var operations = AddDevice(source); var preview = await NetworkWriteTools.NetworkWrite(f.Client, safety, operations);
        var root = Assert.IsType<JsonElement>(preview.StructuredContent); Assert.True(root.GetProperty("success").GetBoolean());
        var token = root.GetProperty("preview").GetProperty("safetyToken").GetString();
        Success(await f.Client.BindOpenProjectAsync(B, true), ProjectBindingTransitions.Switched);
        var count = f.Methods().Length; var applied = await NetworkWriteTools.NetworkWrite(f.Client, safety, operations, true, token);
        var rejection = Assert.IsType<JsonElement>(applied.StructuredContent);
        Assert.Equal(WorkerFailureCategories.BindingConflict, rejection.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(count, f.Methods().Length);
    }
    [Fact]
    public async Task ReadWriteNoProject_BindThenNetworkWritePreview_PassesBindingGate()
    {
        const string source = "C:/Projects/network-roundtrip.ap21";
        using var f = new Fixture(initial: source, mode: McpAccessMode.ReadWrite, entries: new[] { new FakeWorkerPortals.Entry(42, source) });
        using var audit = new TempAuditDirectory(); var safety = audit.CreateSafety(projectSessionBinding: f.Binding);
        var operations = AddDevice(source); var blocked = await NetworkWriteTools.NetworkWrite(f.Client, safety, operations);
        var rejection = Assert.IsType<JsonElement>(blocked.StructuredContent);
        Assert.Equal(WorkerFailureCategories.BindingConflict, rejection.GetProperty("error").GetProperty("category").GetString());
        Assert.Empty(f.Log.Methods());
        Success(await f.Client.BindOpenProjectAsync(source, false), ProjectBindingTransitions.Bound);
        var preview = await NetworkWriteTools.NetworkWrite(f.Client, safety, operations);
        Assert.True(Assert.IsType<JsonElement>(preview.StructuredContent).GetProperty("success").GetBoolean());
    }
    [Fact]
    public async Task Bound_ProjectIsTheVerifyingStatusRead()
    {
        using var f = Two(); var result = await f.Client.BindOpenProjectAsync(B, false);
        Success(result, ProjectBindingTransitions.Bound); Assert.Equal(ProjectPathNormalization.Canonicalize(B), result.Project!.Path);
        Assert.Equal("B", result.Project.Name); Assert.Null(result.Project.Metadata);
        Assert.Equal("get_project_status", f.Methods().Last());
    }
    [Fact]
    public async Task ReadOnly_Bind_SendsOnlyObserveAndSessionSelectionOperations()
    {
        using var f = Two(); Success(await f.Client.BindOpenProjectAsync(B, false), ProjectBindingTransitions.Bound);
        Assert.All(f.Methods(), m => Assert.True(OperationPolicyCatalog.GetCapability(m) is OperationCapability.Observe or OperationCapability.SessionSelection));
    }
    [Fact]
    public async Task WaitsForLeaseAndHonoursCancellation()
    {
        using var f = Two(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var lease = f.Client.ExecuteWithPinnedBindingAsync(f.Binding.CaptureSnapshot(), async () => { entered.SetResult(); await release.Task; return new object(); });
        await entered.Task; using var cancellation = new CancellationTokenSource();
        try
        {
            var pending = f.Client.BindOpenProjectAsync(B, false, cancellation.Token); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending); Assert.Empty(f.Methods());
        }
        finally { release.SetResult(); await lease; }
    }

    [Fact]
    public async Task CancellationAfterSelectionDispatch_DoesNotAbandonSwitch()
    {
        const string target = "C:/Projects/portal-switch-hang.ap21";
        using var f = Two(target: target, timeout: TimeSpan.FromSeconds(1)); await f.Verify();
        using var cancellation = new CancellationTokenSource();
        var pending = f.Client.BindOpenProjectAsync(target, true, cancellation.Token);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var sent = false;
        while (!sent && DateTime.UtcNow < deadline)
        {
            try { sent = f.SelectionDispatched(); }
            catch (IOException) { /* The child may still hold its append handle. */ }
            if (!sent) await Task.Delay(10);
        }
        Assert.Contains("select_portal_project", f.Methods()); cancellation.Cancel();
        Failure(await pending, WorkerFailureCategories.WorkerTimeout, ProjectBindingSnapshot.InvalidatedState);
    }

    [Fact]
    public async Task Switch_TargetAmbiguous_KeepsPreviousAndListsOnce()
    {
        using var f = new Fixture(entries: new[] { new FakeWorkerPortals.Entry(42, A), new FakeWorkerPortals.Entry(43, B), new FakeWorkerPortals.Entry(44, B) });
        await f.Verify(); var result = await f.Client.BindOpenProjectAsync(B, true);
        Failure(result, WorkerFailureCategories.TargetAmbiguous, ProjectBindingSnapshot.VerifiedState);
        Assert.True(result.Before.SameBinding(result.After)); Assert.Equal(3, result.Portals.Count);
        Assert.Equal(new[] { "get_project_status", "select_portal_project", "list_tia_portal_processes" }, f.Methods());
    }

    [Fact]
    public async Task Unbound_MalformedSelection_RetainsUnboundState()
    {
        const string target = "C:/Projects/portal-selection-malformed.ap21";
        using var f = Two(target: target);
        Failure(await f.Client.BindOpenProjectAsync(target, false), WorkerFailureCategories.ProtocolError, ProjectBindingSnapshot.UnboundState);
        Assert.Equal(new[] { "select_portal_project" }, f.Methods());
    }
}
