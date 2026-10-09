using System.Text.Json;
using TiaMcpServer.Contracts.Diagnostics;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class FakeWorkerPortalInventoryTests
{
    private const string Source = @"C:\Fixture\guarded-lifecycle-source.ap21";
    private const string Target = @"C:\Fixture\Target.ap21";

    [Fact]
    public async Task Listing_ReportsInventorySortedAndAttached()
    {
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var portals = new FakeWorkerPortals(new(900, Target), new(200, Source), new(500, null, false));
        using var transport = CreateTransport();
        var response = await List(transport);
        var listing = Payload<TiaPortalProcessListInfo>(response);
        Assert.Equal(200, listing.AttachedProcessId);
        Assert.Equal(new[] { 200, 500, 900 }, listing.Processes.Select(entry => entry.ProcessId));
        Assert.True(listing.Processes[0].AttachedByThisWorker);
        Assert.False(listing.Processes[1].AttachedByThisWorker);
        Assert.False(listing.Processes[1].HasUserInterface);
        Assert.Null(listing.Processes[1].ProjectPath);
        Assert.Equal(Target, listing.Processes[2].ProjectPath);
    }

    [Fact]
    public async Task Select_SwitchesPidPathAndGeneration()
    {
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var portals = new FakeWorkerPortals(new(200, Source, Modified: true), new(900, Target));
        using var transport = CreateTransport();
        var before = Identity(await List(transport));
        var selected = await Select(transport, Target);
        var result = Payload<PortalProjectSelectionInfo>(selected);
        Assert.Equal(200, result.PreviousProcessId);
        Assert.Equal(Source, result.PreviousProjectPath);
        Assert.True(result.PreviousProjectIsModified);
        Assert.False(result.PreviousProjectWasWorkerOpened);
        Assert.True(result.Reattached);
        var after = Identity(selected);
        Assert.Equal(before.WorkerSessionId, after.WorkerSessionId);
        Assert.Equal(900, after.PortalProcessId);
        Assert.Equal(Target, after.ProjectPath);
        Assert.True(after.SessionGeneration > before.SessionGeneration);
        var listing = Payload<TiaPortalProcessListInfo>(await List(transport));
        Assert.Equal(900, listing.AttachedProcessId);
        Assert.True(listing.Processes.Single(entry => entry.ProcessId == 900).AttachedByThisWorker);
        var stale = await transport.SendAsync(new WorkerRequest { Method = "get_project_status", ExpectedSessionIdentity = before });
        Assert.Equal(WorkerFailureCategories.BindingConflict, stale.FailureCategory);
        var status = await transport.SendAsync(new WorkerRequest { Method = "get_project_status", ExpectedSessionIdentity = after });
        Assert.True(status.Success, status.Error);
        Assert.True(Payload<ProjectStatusResultInfo>(status).Project!.IsOpen);
    }

    [Fact]
    public async Task Select_HeadlessModifiedPrevious_GuardBlockedNoChange()
    {
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var portals = new FakeWorkerPortals(new(200, Source, false, Modified: true), new(900, Target));
        using var transport = CreateTransport();
        var before = Identity(await List(transport));
        var rejected = await Select(transport, Target);
        Assert.False(rejected.Success);
        Assert.Equal(WorkerFailureCategories.GuardBlocked, rejected.FailureCategory);
        AssertSame(before, Identity(await List(transport)));
    }

    [Fact]
    public async Task Select_NotAdvertised_TargetNotFoundNoChange()
    {
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(200, Source));
        using var transport = CreateTransport();
        var before = Identity(await List(transport));
        var rejected = await Select(transport, Target);
        Assert.False(rejected.Success);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, rejected.FailureCategory);
        AssertSame(before, Identity(await List(transport)));
    }

    [Fact]
    public async Task Select_Ambiguous_TargetAmbiguousNoChange()
    {
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var portals = new FakeWorkerPortals(new(200, Source), new(500, Target), new(900, Target));
        using var transport = CreateTransport();
        var before = Identity(await List(transport));
        var rejected = await Select(transport, Target);
        Assert.False(rejected.Success);
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, rejected.FailureCategory);
        AssertSame(before, Identity(await List(transport)));
    }

    [Fact]
    public async Task Select_FailsAfterDetach_LeavesUnattached()
    {
        const string target = @"C:\Fixture\portal-switch-fails-after-detach.ap21";
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var portals = new FakeWorkerPortals(new(200, Source), new(900, target));
        using var transport = CreateTransport();
        var before = Identity(await List(transport));
        var response = await Select(transport, target);
        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, response.FailureCategory);
        var after = await List(transport);
        var listing = Payload<TiaPortalProcessListInfo>(after);
        Assert.Null(listing.AttachedProcessId);
        Assert.All(listing.Processes, entry => Assert.False(entry.AttachedByThisWorker));
        Assert.Null(Identity(after).ProjectPath);
        Assert.Null(Identity(after).PortalProcessId);
        Assert.True(Identity(after).SessionGeneration > before.SessionGeneration);
    }

    [Fact]
    public async Task InventoryWithoutUiMatch_StartsUnattached()
    {
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(900, Target));
        using var transport = CreateTransport();
        var response = await List(transport);
        Assert.Null(Payload<TiaPortalProcessListInfo>(response).AttachedProcessId);
        Assert.Null(Identity(response).ProjectPath);
    }

    [Fact]
    public async Task NoInventory_KeepsSinglePortalBehaviour()
    {
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var transport = CreateTransport();
        var response = await List(transport);
        var listing = Payload<TiaPortalProcessListInfo>(response);
        var portal = Assert.Single(listing.Processes);
        Assert.Equal(4242, listing.AttachedProcessId);
        Assert.Equal(4242, portal.ProcessId);
        Assert.Equal(Source, portal.ProjectPath);
        Assert.True(portal.HasUserInterface);
        Assert.True(portal.AttachedByThisWorker);
        Assert.Equal(4242, Identity(response).PortalProcessId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PortalMode_DoesNotManufactureWorkerOwnership(bool hasUi)
    {
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var portals = new FakeWorkerPortals(new(200, Source, hasUi, OtherClients: 1), new(900, Target));
        using var transport = CreateTransport();
        Assert.False(Payload<PortalProjectSelectionInfo>(await Select(transport, Target)).PreviousProjectWasWorkerOpened);
    }

    [Fact]
    public async Task ExplicitOpenOwnership_IsClearedAcrossDetachAndReselect()
    {
        const string opened = @"C:\Fixture\guarded-lifecycle-opened.ap21";
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var portals = new FakeWorkerPortals(new(200, Source), new(900, Target));
        using var transport = new PersistentWorkerTransport(FakeWorkerLocator.Locate(),
            TimeSpan.FromSeconds(5), workerArgs: "--access-mode read-write");
        var open = await transport.SendAsync(new WorkerRequest { Method = "open_project", ProjectPath = opened });
        Assert.True(open.Success, open.Error);
        Assert.True(Payload<PortalProjectSelectionInfo>(await Select(transport, Target)).PreviousProjectWasWorkerOpened);
        var reselect = Payload<PortalProjectSelectionInfo>(await Select(transport, opened));
        Assert.False(reselect.PreviousProjectWasWorkerOpened);
        Assert.False(Payload<PortalProjectSelectionInfo>(await Select(transport, Target)).PreviousProjectWasWorkerOpened);
    }

    [Theory]
    [InlineData(true, "guarded-lifecycle-opened.ap21", false)]
    [InlineData(false, "guarded-lifecycle-opened-modified.ap21", true)]
    public async Task OpenReplacement_UsesOpenedProjectModifiedStateForStatusAndDetach(
        bool sourceModified, string openedFile, bool openedModified)
    {
        var opened = Path.Combine(@"C:\Fixture", openedFile);
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var portals = new FakeWorkerPortals(new(200, Source, false, Modified: sourceModified), new(900, Target));
        using var transport = new PersistentWorkerTransport(FakeWorkerLocator.Locate(),
            TimeSpan.FromSeconds(5), workerArgs: "--access-mode read-write");
        var before = Identity(await List(transport));
        var open = await transport.SendAsync(new WorkerRequest { Method = "open_project", ProjectPath = opened });
        Assert.Equal(openedModified, Payload<ProjectLifecycleResultInfo>(open).Project!.IsModified);
        Assert.Equal(opened, Identity(open).ProjectPath);
        Assert.True(Identity(open).SessionGeneration > before.SessionGeneration);
        var status = await transport.SendAsync(new WorkerRequest { Method = "get_project_status" });
        Assert.Equal(openedModified, Payload<ProjectStatusResultInfo>(status).Project!.IsModified);
        var selection = await Select(transport, Target);
        if (openedModified)
        {
            Assert.False(selection.Success);
            Assert.Equal(WorkerFailureCategories.GuardBlocked, selection.FailureCategory);
            AssertSame(Identity(open), Identity(await List(transport)));
        }
        else
        {
            var previous = Payload<PortalProjectSelectionInfo>(selection);
            Assert.False(previous.PreviousProjectIsModified);
            Assert.True(previous.PreviousProjectWasWorkerOpened);
            Assert.Equal(900, Identity(selection).PortalProcessId);
        }
    }

    [Fact]
    public async Task OpenAlreadyOpenProject_PreservesModifiedStateAndDetachGuard()
    {
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var portals = new FakeWorkerPortals(new(200, Source, false, Modified: true), new(900, Target));
        using var transport = new PersistentWorkerTransport(FakeWorkerLocator.Locate(),
            TimeSpan.FromSeconds(5), workerArgs: "--access-mode read-write");
        var before = Identity(await List(transport));
        var open = await transport.SendAsync(new WorkerRequest { Method = "open_project", ProjectPath = Source });
        Assert.True(open.Success, open.Error);
        var status = await transport.SendAsync(new WorkerRequest { Method = "get_project_status" });
        Assert.True(Payload<ProjectStatusResultInfo>(status).Project!.IsModified);
        var selection = await Select(transport, Target);
        Assert.Equal(WorkerFailureCategories.GuardBlocked, selection.FailureCategory);
        AssertSame(before, Identity(await List(transport)));
    }

    [Fact]
    public async Task EmptyInventory_ReportsNoPortalsAndStartsUnattached()
    {
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var portals = new FakeWorkerPortals();
        using var transport = CreateTransport();
        var response = await List(transport);
        var listing = Payload<TiaPortalProcessListInfo>(response);
        Assert.Empty(listing.Processes);
        Assert.Null(listing.AttachedProcessId);
        Assert.Null(Identity(response).PortalProcessId);
        Assert.Null(Identity(response).ProjectPath);
    }

    [Fact]
    public async Task EmptyInventoryFixture_RestoresPreviousInventoryOnDispose()
    {
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var outer = new FakeWorkerPortals(new FakeWorkerPortals.Entry(200, Source));
        using (var empty = new FakeWorkerPortals())
        using (var transport = CreateTransport())
            Assert.Empty(Payload<TiaPortalProcessListInfo>(await List(transport)).Processes);
        using var restored = CreateTransport();
        var listing = Payload<TiaPortalProcessListInfo>(await List(restored));
        Assert.Equal(200, Assert.Single(listing.Processes).ProcessId);
        Assert.Equal(200, listing.AttachedProcessId);
    }

    [Fact]
    public async Task OpenAlreadyUiOpenProject_DoesNotManufactureOwnership()
    {
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var portals = new FakeWorkerPortals(new(200, Source), new(900, Target));
        using var transport = new PersistentWorkerTransport(FakeWorkerLocator.Locate(),
            TimeSpan.FromSeconds(5), workerArgs: "--access-mode read-write");
        var response = await transport.SendAsync(new WorkerRequest { Method = "open_project", ProjectPath = Source });
        Assert.True(response.Success, response.Error);
        Assert.False(Payload<PortalProjectSelectionInfo>(await Select(transport, Target)).PreviousProjectWasWorkerOpened);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task ModifiedPrevious_WithUiOrOtherClient_CanDetach(bool hasUi, int otherClients)
    {
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var portals = new FakeWorkerPortals(new(200, Source, hasUi, otherClients, Modified: true), new(900, Target));
        using var transport = CreateTransport();
        Assert.True((await Select(transport, Target)).Success);
        Assert.Equal(900, Identity(await List(transport)).PortalProcessId);
    }

    [Fact]
    public async Task Select_MalformedScenario_ReturnsInvalidPayloadAfterSwitch()
    {
        const string target = @"C:\Fixture\portal-selection-malformed.ap21";
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var portals = new FakeWorkerPortals(new(200, Source), new(900, target));
        using var transport = CreateTransport();
        var response = await Select(transport, target);
        Assert.True(response.Success, response.Error);
        using var document = JsonDocument.Parse(response.Payload!);
        Assert.True(document.RootElement.GetProperty("untrustedMarker").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("reattached", out _));
        Assert.Equal(target, Identity(response).ProjectPath);
        Assert.Equal(900, Identity(response).PortalProcessId);
    }

    [Theory]
    [InlineData("portal-switch-hang.ap21", true)]
    [InlineData("portal-switch-crash.ap21", false)]
    public async Task Select_TransportFailureScenario_InterruptsResponse(string file, bool timeout)
    {
        var target = Path.Combine(@"C:\Fixture", file);
        using var ui = new FakeWorkerUiOpenProject(Source);
        using var portals = new FakeWorkerPortals(new(200, Source), new(900, target));
        using var transport = CreateTransport();
        await List(transport); // Handshake completes before the injected failure.
        if (timeout) await Assert.ThrowsAsync<TimeoutException>(() => Select(transport, target));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => Select(transport, target));
    }

    private static PersistentWorkerTransport CreateTransport() => new(FakeWorkerLocator.Locate(),
        TimeSpan.FromSeconds(5), workerArgs: "--access-mode read-only");
    private static Task<WorkerResponse> List(PersistentWorkerTransport transport)
        => transport.SendAsync(new WorkerRequest { Method = "list_tia_portal_processes" });
    private static Task<WorkerResponse> Select(PersistentWorkerTransport transport, string path)
        => transport.SendAsync(new WorkerRequest { Method = "select_portal_project", ProjectPath = path });
    private static T Payload<T>(WorkerResponse response)
    {
        Assert.True(response.Success, response.Error);
        return JsonSerializer.Deserialize<T>(response.Payload!, WorkerJson.Envelope)!;
    }
    private static WorkerSessionIdentity Identity(WorkerResponse response)
        => Assert.IsType<WorkerSessionIdentity>(response.SessionIdentity);
    private static void AssertSame(WorkerSessionIdentity before, WorkerSessionIdentity after)
    {
        Assert.Equal(before.WorkerSessionId, after.WorkerSessionId);
        Assert.Equal(before.SessionGeneration, after.SessionGeneration);
        Assert.Equal(before.PortalProcessId, after.PortalProcessId);
        Assert.Equal(before.ProjectPath, after.ProjectPath);
    }
}
