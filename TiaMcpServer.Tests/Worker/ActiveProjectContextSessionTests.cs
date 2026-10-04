using Siemens.Engineering;
using Siemens.Engineering.Multiuser;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;
using Session = TiaMcpServer.OpennessWorker.Openness.TiaPortalSession;
using PortalProject = Siemens.Engineering.Project;

namespace TiaMcpServer.Tests.Worker;

[CollectionDefinition("Portal session boundary", DisableParallelization = true)]
public sealed class PortalSessionBoundaryCollection { }

[Collection("Portal session boundary")]
public sealed class ActiveProjectContextSessionTests
{
    [Fact]
    public void SameRootAdoption_DoesNotAdvanceGeneration()
    {
        using var f = new Fixture();
        var project = f.A.Portal.Projects.Items[0];
        f.Session.AdoptContext(ActiveProjectContext.ForStandalone(project, true), f.PathA);
        var before = f.Session.GetSessionIdentity();
        f.Session.AdoptContext(ActiveProjectContext.ForStandalone(project, false), f.PathA);
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        Assert.False(f.Session.ActiveContext!.Owner.OpenedByWorker);
        Assert.Same(project, f.Session.Project);
        Assert.Same(project, f.Session.EngineeringRoot);
    }

    [Fact]
    public void DifferentRootAtSamePath_AdvancesGeneration()
    {
        using var f = new Fixture();
        f.Session.Connect(f.PathA);
        var before = f.Session.GetSessionIdentity();
        var replacement = new PortalProject { Path = new FileInfo(f.PathA) };
        f.Session.AdoptContext(ActiveProjectContext.ForStandalone(replacement, false), f.PathA);
        Assert.Equal(before.SessionGeneration + 1, f.Session.GetSessionIdentity().SessionGeneration);
        AssertCategory(WorkerFailureCategories.BindingConflict,
            () => f.Session.ValidateExpectedSessionIdentity(before, false));
    }

    [Fact]
    public void SaveAsOnSameRoot_AdvancesGenerationOnce()
    {
        using var f = new Fixture();
        f.Session.Connect(f.PathA);
        var before = f.Session.GetSessionIdentity();
        f.Session.Project!.Path = new FileInfo(f.PathB);
        f.Session.AcceptCurrentProjectIdentity();
        var after = f.Session.GetSessionIdentity();
        Assert.Equal(before.SessionGeneration + 1, after.SessionGeneration);
        Assert.Equal(f.PathB, after.ProjectPath);
        f.Session.AcceptCurrentProjectIdentity();
        Assert.Equal(after.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnreadableOrBlankPath_ClearsContextWithoutFallback(bool unreadable)
    {
        using var f = new Fixture();
        f.Session.Connect(f.PathA);
        var source = f.Session.Project!;
        f.Session.TrackWorkerOpenedProject(source);
        var before = f.Session.GetSessionIdentity();
        if (unreadable) source.PathFailure = new EngineeringException("stale");
        else source.Path = null;
        f.A.Portal.Projects.Items.Clear();
        f.A.Portal.Projects.Items.Add(new PortalProject { Path = new FileInfo(f.PathB) });
        Assert.Null(f.Session.GetSessionIdentity().ProjectPath);
        Assert.Null(f.Session.ActiveContext);
        Assert.Equal(before.SessionGeneration + 1, f.Session.GetSessionIdentity().SessionGeneration);
        f.Session.EnsureConnected(null);
        Assert.Null(f.Session.Project);
        Assert.Equal(before.SessionGeneration + 1, f.Session.GetSessionIdentity().SessionGeneration);
        AssertCategory(WorkerFailureCategories.BindingConflict,
            () => f.Session.ValidateExpectedSessionIdentity(before, false));
        Assert.Equal(0, source.CloseCalls);
    }

    [Fact]
    public void ExternalPathChange_RejectsOldIdentity()
    {
        using var f = new Fixture();
        f.Session.Connect(f.PathA);
        var before = f.Session.GetSessionIdentity();
        f.Session.Project!.Path = new FileInfo(f.PathB);
        AssertCategory(WorkerFailureCategories.BindingConflict, () => f.Session.EnsureConnected(f.PathA));
        Assert.Equal(before.SessionGeneration + 1, f.Session.GetSessionIdentity().SessionGeneration);
        AssertCategory(WorkerFailureCategories.BindingConflict,
            () => f.Session.ValidateExpectedSessionIdentity(before, false));
    }

    [Fact]
    public void ExactSelection_RejectsMissingAndAmbiguousTargetsBeforeDetach()
    {
        using var f = new Fixture();
        f.Session.Connect(f.PathA);
        var before = f.Session.GetSessionIdentity();
        var context = f.Session.ActiveContext;
        AssertCategory(WorkerFailureCategories.TargetNotFound, () => f.Session.SelectPortalProject(f.File("missing.ap21")));
        f.Register(3, f.PathB);
        AssertCategory(WorkerFailureCategories.TargetAmbiguous, () => f.Session.SelectPortalProject(f.PathB));
        Assert.Same(context, f.Session.ActiveContext);
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        Assert.Equal(before.PortalProcessId, f.Session.CurrentProcessId);
        Assert.Equal(0, f.A.Portal.DisposeCalls);
        Assert.Equal(0, f.B.AttachCalls);
        f.AssertNoLifecycle();
    }

    [Fact]
    public void CleanWorkerOwnedOpen_ClosesThenOpens()
    {
        using var f = new Fixture();
        f.Session.Connect(f.PathA);
        var source = f.Session.Project!;
        f.Session.TrackWorkerOpenedProject(source);
        var before = f.Session.GetSessionIdentity();
        f.A.Portal.Projects.Calls.Clear();
        f.Session.OpenProject(f.PathB);
        Assert.Equal(new[] { "IsModified", "Close", "Open" }, f.A.Portal.Projects.Calls);
        Assert.Equal(1, source.CloseCalls);
        Assert.True(f.Session.ActiveContext!.Owner.OpenedByWorker);
        Assert.Equal(before.SessionGeneration + 2, f.Session.GetSessionIdentity().SessionGeneration);
        Assert.Equal(f.PathB, f.Session.CurrentProjectPath);
    }

    [Theory]
    [InlineData(false, WorkerFailureCategories.StateChanged)]
    [InlineData(true, WorkerFailureCategories.WorkerOperationFailed)]
    public void DirtyOrUnreadableWorkerOwnedOpen_DoesNotCloseOrOpen(bool unreadable, string category)
    {
        using var f = new Fixture();
        f.Session.Connect(f.PathA);
        var source = f.Session.Project!;
        f.Session.TrackWorkerOpenedProject(source);
        source.IsModified = true;
        if (unreadable) source.ModifiedFailure = new EngineeringException("unknown");
        var before = f.Session.GetSessionIdentity();
        var context = f.Session.ActiveContext;
        AssertCategory(category, () => f.Session.OpenProject(f.PathB));
        Assert.Same(context, f.Session.ActiveContext);
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        f.AssertNoLifecycle();
    }

    [Fact]
    public void CloseFailure_DoesNotOpenReplacement()
    {
        using var f = new Fixture();
        f.Session.Connect(f.PathA);
        var source = f.Session.Project!;
        f.Session.TrackWorkerOpenedProject(source);
        source.CloseFailure = new EngineeringException("close refused");
        var before = f.Session.GetSessionIdentity();
        var context = f.Session.ActiveContext;
        Assert.Throws<InvalidOperationException>(() => f.Session.OpenProject(f.PathB));
        Assert.Same(context, f.Session.ActiveContext);
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        Assert.Equal(1, source.CloseCalls);
        Assert.Equal(0, f.A.Portal.Projects.OpenCalls);
    }

    [Fact]
    public void UiOwnedOpen_LeavesSourceOpen()
    {
        using var f = new Fixture();
        f.Session.Connect(f.PathA);
        var source = f.Session.Project!;
        f.Session.OpenProject(f.PathB);
        Assert.Equal(0, source.CloseCalls);
        Assert.Equal(0, source.SaveCalls);
        Assert.Contains(source, f.A.Portal.Projects.Items);
        Assert.True(f.Session.ActiveContext!.Owner.OpenedByWorker);
    }

    [Fact]
    public void SwitchAndReattach_DoesNotRestoreOwnership()
    {
        using var f = new Fixture();
        f.Session.Connect(f.PathA);
        f.Session.TrackWorkerOpenedProject(f.Session.Project!);
        var before = f.Session.GetSessionIdentity();
        f.Session.SelectPortalProject(f.PathB);
        Assert.False(f.Session.ActiveContext!.Owner.OpenedByWorker);
        Assert.True(f.Session.GetSessionIdentity().SessionGeneration > before.SessionGeneration);
        f.Session.SelectPortalProject(f.PathA);
        Assert.False(f.Session.ActiveContext!.Owner.OpenedByWorker);
        Assert.Equal(f.A.Id, f.Session.CurrentProcessId);
        f.AssertNoLifecycle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeadlessLastClient_UnknownOrModifiedStateBlocksDetach(bool unreadable)
    {
        using var f = new Fixture();
        f.A.Mode = TiaPortalMode.WithoutUserInterface;
        // The selected source is clean; a second attached project must still block detach.
        var other = new PortalProject { Path = new FileInfo(f.File("other.ap21")), IsModified = true };
        if (unreadable) other.ModifiedFailure = new EngineeringException("unreadable");
        f.A.Portal.Projects.Items.Add(other);
        f.Session.Connect(f.PathA);
        var before = f.Session.GetSessionIdentity();
        AssertCategory(WorkerFailureCategories.GuardBlocked, () => f.Session.SelectPortalProject(f.PathB));
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        Assert.Equal(0, f.A.Portal.DisposeCalls);
        Assert.Equal(0, f.B.AttachCalls);
        f.AssertNoLifecycle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttachFailureAfterDetach_CannotReturnFormerIdentity(bool disposeFailure)
    {
        using var f = new Fixture();
        f.Session.Connect(f.PathA);
        var before = f.Session.GetSessionIdentity();
        if (disposeFailure) f.A.Portal.DisposeFailure = new InvalidOperationException("dispose");
        else f.B.AttachFailure = new EngineeringException("attach");
        AssertCategory(WorkerFailureCategories.WorkerOperationFailed, () => f.Session.SelectPortalProject(f.PathB));
        Assert.Null(f.Session.ActiveContext);
        Assert.Null(f.Session.GetSessionIdentity().ProjectPath);
        Assert.Null(f.Session.CurrentProcessId);
        Assert.True(f.Session.GetSessionIdentity().SessionGeneration > before.SessionGeneration);
        f.AssertNoLifecycle();
    }

    [Theory]
    [InlineData(ProjectContainerKinds.StandaloneProject)]
    [InlineData(ProjectContainerKinds.LocalSession)]
    [InlineData(ProjectContainerKinds.ServerProject)]
    public void DisconnectAndDispose_ReleaseWithoutLifecycleCalls(string kind)
    {
        using var f = new Fixture();
        f.Session.Connect(f.PathA);
        var local = new LocalSession { Project = new MultiuserProject { Path = new FileInfo(f.PathB) } };
        if (kind != ProjectContainerKinds.StandaloneProject)
            f.Session.AdoptContext(ActiveProjectContext.ForLocalSession(local, true, kind, MultiuserSessionModes.Unknown), f.PathB);
        f.Session.Disconnect();
        f.Session.Dispose();
        f.Session.Dispose();
        Assert.Null(f.Session.ActiveContext);
        Assert.Null(f.Session.CurrentProcessId);
        Assert.Equal(1, f.A.Portal.DisposeCalls);
        Assert.Equal(0, f.A.Portal.SubscribersAtDispose);
        Assert.Equal(0, local.SaveCalls);
        Assert.Equal(0, local.CloseCalls);
        Assert.Equal(0, local.CommitCalls);
        f.AssertNoLifecycle();
    }

    [Fact]
    public void FailedAdoption_DoesNotReplaceCurrentContext()
    {
        using var f = new Fixture();
        f.Session.Connect(f.PathA);
        var context = f.Session.ActiveContext;
        var before = f.Session.GetSessionIdentity();
        AssertCategory(WorkerFailureCategories.PostconditionFailed, () => f.Session.AdoptContext(
            ActiveProjectContext.ForStandalone(new PortalProject { Path = new FileInfo(f.PathB) }, false), f.PathA));
        Assert.Same(context, f.Session.ActiveContext);
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        f.AssertNoLifecycle();
    }

    private static void AssertCategory(string category, Action action)
        => Assert.Equal(category, Assert.Throws<WorkerOperationException>(action).FailureCategory);

    private sealed class Fixture : IDisposable
    {
        private readonly TiaPortalProcess[] previous = TiaPortal.Processes.ToArray();
        private readonly string directory = Path.Combine(Path.GetTempPath(), "pr2-session-" + Guid.NewGuid().ToString("N"));
        public Session Session { get; } = new();
        public TiaPortalProcess A { get; }
        public TiaPortalProcess B { get; }
        public string PathA { get; }
        public string PathB { get; }
        public Fixture()
        {
            Directory.CreateDirectory(directory);
            TiaPortal.Processes.Clear();
            PathA = File("A.ap21"); PathB = File("B.ap21");
            A = Register(101, PathA); B = Register(202, PathB);
        }
        public string File(string name)
        {
            var path = Path.Combine(directory, name);
            System.IO.File.WriteAllText(path, "offline fixture");
            return path;
        }
        public TiaPortalProcess Register(int id, string path)
        {
            var process = new TiaPortalProcess { Id = id, ProjectPath = new FileInfo(path) };
            process.Portal = new TiaPortal { Process = process };
            var project = new PortalProject { Path = new FileInfo(path), Calls = process.Portal.Projects.Calls };
            project.OnClose = () => process.Portal.Projects.Items.Remove(project);
            process.Portal.Projects.Items.Add(project);
            TiaPortal.Processes.Add(process);
            return process;
        }
        public void AssertNoLifecycle()
        {
            foreach (var process in TiaPortal.Processes)
            {
                Assert.Equal(0, process.Portal.Projects.OpenCalls);
                foreach (var project in process.Portal.Projects.Items)
                {
                    Assert.Equal(0, project.CloseCalls);
                    Assert.Equal(0, project.SaveCalls);
                }
            }
        }
        public void Dispose()
        {
            try { Session.Dispose(); }
            finally
            {
                TiaPortal.Processes.Clear(); TiaPortal.Processes.AddRange(previous);
                Directory.Delete(directory, true);
            }
        }
    }
}
