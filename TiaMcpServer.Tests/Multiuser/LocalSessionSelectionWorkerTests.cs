using Siemens.Engineering;
using Siemens.Engineering.Multiuser;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;
using Session = TiaMcpServer.OpennessWorker.Openness.TiaPortalSession;
using PortalProject = Siemens.Engineering.Project;

namespace TiaMcpServer.Tests.Multiuser;

[Collection("Portal session boundary")]
public sealed class LocalSessionSelectionWorkerTests
{
    [Fact]
    public void AmcAdoption_UsesTypedOwnerPath()
    {
        using var f = new Fixture();
        var owner = f.AddLocal(f.A, f.Amc);
        f.Session.Connect(f.Amc);
        var identity = f.Session.GetSessionIdentity();
        Assert.Equal(f.Amc, identity.ProjectPath);
        Assert.Equal(f.A.Id, identity.PortalProcessId);
        Assert.Equal(f.Amc, identity.Context?.EngineeringProjectPath);
        Assert.Null(identity.Context?.SessionContainerPath);
        Assert.Same(owner.Project, f.Session.EngineeringRoot);
        Assert.False(f.Session.ActiveContext!.Owner.OpenedByWorker);
        Assert.Null(f.Session.ActiveContext.GetType().GetProperty("SessionContainerPath")?.GetValue(f.Session.ActiveContext));
        Assert.Equal(0, f.A.Portal.LocalSessions.OpenCalls);
    }

    [Fact]
    public void MixedStandaloneAndSessionCandidates_AreAmbiguousWithoutPath()
    {
        using var f = new Fixture();
        f.AddLocal(f.A, f.Amc);
        f.AddStandalone(f.A, f.Ap);
        AssertCategory(WorkerFailureCategories.TargetAmbiguous, () => f.Session.Connect(null));
        Assert.Null(f.Session.ActiveContext);
    }

    [Fact]
    public void MissingAndDuplicateExactEngineeringPath_NeverPickFirst()
    {
        using var f = new Fixture();
        Assert.Null(f.Session.CurrentProjectPath);
        f.Session.Connect(f.Amc);
        Assert.Null(f.Session.ActiveContext);
        f.AddLocal(f.A, f.Amc);
        f.AddLocal(f.A, f.Amc);
        AssertCategory(WorkerFailureCategories.TargetAmbiguous, () => f.Session.EnsureConnected(f.Amc));
        Assert.Equal(0, f.A.Portal.LocalSessions.OpenCalls);
    }

    [Fact]
    public void AlsInput_IsNotColdAdoptionIdentity()
    {
        using var f = new Fixture();
        f.AddLocal(f.A, f.Amc);
        f.Session.Connect(f.Als);
        Assert.Null(f.Session.ActiveContext);
        Assert.Null(f.Session.CurrentProjectPath);
    }

    [Fact]
    public void SameSessionReverify_PreservesGenerationAndOwner()
    {
        using var f = new Fixture();
        f.AddLocal(f.A, f.Amc);
        f.Session.Connect(f.Amc);
        var before = f.Session.GetSessionIdentity();
        var context = f.Session.ActiveContext;
        f.Session.EnsureConnected(f.Amc);
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        Assert.Same(context, f.Session.ActiveContext);
        Assert.False(f.Session.ActiveContext!.Owner.OpenedByWorker);
    }

    [Fact]
    public void SameOwnerProxyWrappers_PreserveGenerationAndOpenerProvenance()
    {
        using var f = new Fixture();
        f.A.Portal.LocalSessions.NextProjectPath = new FileInfo(f.Amc);
        f.Session.OpenProject(f.Als);
        var before = f.Session.GetSessionIdentity();
        var original = Assert.Single(f.A.Portal.LocalSessions.Items);
        original.EqualityToken = "owner-1";
        original.Project.EqualityToken = "root-1";
        f.A.Portal.LocalSessions.Items[0] = new LocalSession
        {
            EqualityToken = "owner-1",
            Project = new MultiuserProject { EqualityToken = "root-1", Path = new FileInfo(f.Amc) }
        };
        f.Session.EnsureConnected(f.Amc);
        var after = f.Session.GetSessionIdentity();
        Assert.Equal(before.SessionGeneration, after.SessionGeneration);
        Assert.Equal(f.Als, after.Context?.SessionContainerPath);
        Assert.True(after.Context?.OpenedByWorker);
    }

    [Fact]
    public void ReplacedOwnerAtSameEngineeringPath_InvalidatesOldIdentity()
    {
        using var f = new Fixture();
        f.AddLocal(f.A, f.Amc);
        f.Session.Connect(f.Amc);
        var before = f.Session.GetSessionIdentity();
        f.A.Portal.LocalSessions.Items.Clear();
        f.AddLocal(f.A, f.Amc);
        AssertCategory(WorkerFailureCategories.BindingConflict, () => f.Session.ValidateExpectedSessionIdentity(before, false));
        Assert.NotEqual(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
    }

    [Fact]
    public void UnprovenOwnerPath_IsRejected()
    {
        using var f = new Fixture();
        f.AddLocal(f.A, null);
        AssertCategory(WorkerFailureCategories.PostconditionFailed, () => f.Session.Connect(null));
        Assert.Null(f.Session.ActiveContext);
    }

    [Fact]
    public void AdoptionNeverOwnsOrMutates()
    {
        using var f = new Fixture();
        var owner = f.AddLocal(f.A, f.Amc);
        f.Session.Connect(f.Amc);
        f.Session.EnsureConnected(f.Amc);
        Assert.False(f.Session.ActiveContext!.Owner.OpenedByWorker);
        Assert.Equal(0, f.A.Portal.Projects.OpenCalls);
        Assert.Equal(0, f.A.Portal.LocalSessions.OpenCalls);
        Assert.Equal(0, owner.SaveCalls + owner.CloseCalls + owner.CommitCalls);
    }

    [Fact]
    public void ExactPortalProcessId_SelectsOnlyThatPortalBeforeOwnerEnumeration()
    {
        using var f = new Fixture();
        var other = f.Register(202, f.Amc);
        f.AddLocal(f.A, f.Amc);
        var selected = f.AddLocal(other, f.Amc);
        f.Session.Connect(f.Amc, other.Id);
        Assert.Same(selected.Project, f.Session.EngineeringRoot);
        Assert.Equal(other.Id, f.Session.CurrentProcessId);
        Assert.Equal(0, f.A.AttachCalls);
        Assert.Equal(1, other.AttachCalls);
    }

    [Fact]
    public void InitialAttachWrongPid_HeadlessLocalHandleIsNotUnsafelyDisposed()
    {
        using var f = new Fixture();
        var returnedProcess = f.Register(202, f.Amc);
        returnedProcess.Mode = TiaPortalMode.WithoutUserInterface;
        f.AddLocal(f.A, f.Amc);
        f.A.Portal.Process = returnedProcess;
        try
        {
            AssertCategory(WorkerFailureCategories.GuardBlocked,
                () => f.Session.EnsurePortalConnected(f.A.Id));
            Assert.Equal(0, f.A.Portal.DisposeCalls);
            Assert.Equal(1, f.A.AttachCalls);
            Assert.Null(f.Session.CurrentProcessId);
            AssertCategory(WorkerFailureCategories.GuardBlocked,
                () => f.Session.EnsurePortalConnected(f.A.Id));
            Assert.Equal(1, f.A.AttachCalls);

            returnedProcess.Mode = TiaPortalMode.WithUserInterface;
            f.Session.Disconnect();
            Assert.Equal(1, f.A.Portal.DisposeCalls);
        }
        finally { returnedProcess.Mode = TiaPortalMode.WithUserInterface; }
    }

    [Fact]
    public void InitialAttachUnreadableProcess_RetainsHandleUntilGuardedRecovery()
    {
        using var f = new Fixture();
        f.A.Mode = TiaPortalMode.WithoutUserInterface;
        f.AddLocal(f.A, f.Amc);
        f.A.Portal.CurrentProcessFailure = new EngineeringException("unreadable");
        try
        {
            AssertCategory(WorkerFailureCategories.GuardBlocked, () => f.Session.Connect(f.Amc));
            Assert.Equal(0, f.A.Portal.DisposeCalls);
            Assert.Equal(1, f.A.AttachCalls);
            Assert.Null(f.Session.CurrentProcessId);
            AssertCategory(WorkerFailureCategories.GuardBlocked,
                () => f.Session.EnsurePortalConnected(f.A.Id));
            Assert.Equal(1, f.A.AttachCalls);

            f.A.Portal.CurrentProcessFailure = null;
            f.A.Mode = TiaPortalMode.WithUserInterface;
            f.Session.Disconnect();
            Assert.Equal(1, f.A.Portal.DisposeCalls);
            f.Session.EnsurePortalConnected(f.A.Id);
            Assert.Equal(f.A.Id, f.Session.CurrentProcessId);
        }
        finally
        {
            f.A.Portal.CurrentProcessFailure = null;
            f.A.Mode = TiaPortalMode.WithUserInterface;
        }
    }

    [Fact]
    public void InvalidPortalProcessId_IsRejectedEvenWhenAttached()
    {
        using var f = new Fixture();
        f.AddLocal(f.A, f.Amc);
        f.Session.Connect(f.Amc);
        var before = f.Session.GetSessionIdentity();
        AssertCategory(WorkerFailureCategories.ValidationError, () => f.Session.EnsureConnected(f.Amc, 0));
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
    }

    [Theory]
    [InlineData(false, WorkerFailureCategories.TargetNotFound)]
    [InlineData(true, WorkerFailureCategories.TargetAmbiguous)]
    public void CrossPidMissingOrDuplicateOwner_PreservesVerifiedSource(bool duplicate, string category)
    {
        using var f = new Fixture();
        var source = f.AddLocal(f.A, f.Amc);
        var target = f.Register(202, f.OtherAmc);
        if (duplicate)
        {
            f.AddLocal(target, f.OtherAmc);
            f.AddLocal(target, f.OtherAmc);
        }
        f.Session.Connect(f.Amc);
        var before = f.Session.GetSessionIdentity();
        AssertCategory(category, () => f.Session.SelectPortalProject(f.OtherAmc, target.Id));
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        Assert.Equal(before.PortalProcessId, f.Session.CurrentProcessId);
        Assert.Same(source.Project, f.Session.EngineeringRoot);
        Assert.Equal(0, f.A.Portal.DisposeCalls);
        Assert.Equal(1, target.AttachCalls);
        Assert.Equal(1, target.Portal.DisposeCalls);
    }

    [Fact]
    public void CrossPidHeadlessTargetWithoutRetainer_BlocksBeforeTemporaryAttach()
    {
        using var f = new Fixture();
        f.AddLocal(f.A, f.Amc);
        var target = f.Register(202, f.OtherAmc);
        target.Mode = TiaPortalMode.WithoutUserInterface;
        f.AddLocal(target, f.OtherAmc);
        f.Session.Connect(f.Amc);
        var before = f.Session.GetSessionIdentity();
        AssertCategory(WorkerFailureCategories.GuardBlocked,
            () => f.Session.SelectPortalProject(f.OtherAmc, target.Id));
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        Assert.Equal(0, f.A.Portal.DisposeCalls);
        Assert.Equal(0, target.AttachCalls);
    }

    [Fact]
    public void CrossPidTargetLosesRetainerDuringProbe_KeepsBothAttachments()
    {
        using var f = new Fixture();
        f.AddLocal(f.A, f.Amc);
        var target = f.Register(202, f.OtherAmc);
        f.AddLocal(target, f.OtherAmc);
        target.Portal.LocalSessions.OnEnumerate = _ => target.Mode = TiaPortalMode.WithoutUserInterface;
        f.Session.Connect(f.Amc);
        var before = f.Session.GetSessionIdentity();
        AssertCategory(WorkerFailureCategories.GuardBlocked,
            () => f.Session.SelectPortalProject(f.Amc, target.Id));
        Assert.Equal(0, f.A.Portal.DisposeCalls);
        Assert.Equal(0, target.Portal.DisposeCalls);
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        target.Mode = TiaPortalMode.WithUserInterface;
        f.Session.Disconnect();
        Assert.Equal(1, target.Portal.DisposeCalls);
        Assert.Equal(1, f.A.Portal.DisposeCalls);
    }

    [Fact]
    public void CrossPidVerifiedLocalTarget_SelectsAfterSafeSourceRelease()
    {
        using var f = new Fixture();
        f.AddLocal(f.A, f.Amc);
        var target = f.Register(202, f.OtherAmc);
        var selected = f.AddLocal(target, f.OtherAmc);
        f.Session.Connect(f.Amc);
        f.Session.SelectPortalProject(f.OtherAmc, target.Id);
        Assert.Equal(1, f.A.Portal.DisposeCalls);
        Assert.Equal(0, target.Portal.DisposeCalls);
        Assert.Equal(target.Id, f.Session.CurrentProcessId);
        Assert.Same(selected.Project, f.Session.EngineeringRoot);
    }

    [Fact]
    public void CrossPidTargetOwnerReplacementDuringProbe_PreservesSource()
    {
        using var f = new Fixture();
        f.AddLocal(f.A, f.Amc);
        var target = f.Register(202, f.OtherAmc);
        f.AddLocal(target, f.OtherAmc);
        target.Portal.LocalSessions.OnEnumerate = count =>
        {
            if (count == 2)
            {
                target.Portal.LocalSessions.Items.Clear();
                f.AddLocal(target, f.OtherAmc);
            }
        };
        f.Session.Connect(f.Amc);
        var before = f.Session.GetSessionIdentity();
        AssertCategory(WorkerFailureCategories.BindingConflict,
            () => f.Session.SelectPortalProject(f.OtherAmc, target.Id));
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        Assert.Equal(0, f.A.Portal.DisposeCalls);
        Assert.Equal(1, target.Portal.DisposeCalls);
    }

    [Fact]
    public void CrossPidSourceLastHeadlessLocalClient_BlocksAfterTargetProof()
    {
        using var f = new Fixture();
        f.A.Mode = TiaPortalMode.WithoutUserInterface;
        f.AddLocal(f.A, f.Amc);
        var target = f.Register(202, f.OtherAmc);
        f.AddLocal(target, f.OtherAmc);
        f.Session.Connect(f.Amc);
        var before = f.Session.GetSessionIdentity();
        AssertCategory(WorkerFailureCategories.GuardBlocked,
            () => f.Session.SelectPortalProject(f.OtherAmc, target.Id));
        Assert.Equal(0, f.A.Portal.DisposeCalls);
        Assert.Equal(1, target.Portal.DisposeCalls);
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        f.A.Mode = TiaPortalMode.WithUserInterface;
    }

    [Fact]
    public void DirectRelease_HeadlessLastLocalClientRetainsPortalAndIdentity()
    {
        using var f = new Fixture();
        f.A.Mode = TiaPortalMode.WithoutUserInterface;
        f.AddLocal(f.A, f.Amc);
        f.Session.Connect(f.Amc);
        var before = f.Session.GetSessionIdentity();
        AssertCategory(WorkerFailureCategories.GuardBlocked, () => f.Session.Disconnect());
        AssertCategory(WorkerFailureCategories.GuardBlocked, () => f.Session.Dispose());
        Assert.Equal(0, f.A.Portal.DisposeCalls);
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        Assert.Equal(before.PortalProcessId, f.Session.CurrentProcessId);
        f.A.Mode = TiaPortalMode.WithUserInterface;
    }

    [Fact]
    public void RefusedDispose_KeepsSessionUsableUntilSafeIdempotentRelease()
    {
        using var f = new Fixture();
        f.A.Mode = TiaPortalMode.WithoutUserInterface;
        f.AddLocal(f.A, f.Amc);
        try
        {
            f.Session.Connect(f.Amc);
            var before = f.Session.GetSessionIdentity();
            AssertCategory(WorkerFailureCategories.GuardBlocked, () => f.Session.Dispose());
            Assert.Equal(0, f.A.Portal.DisposeCalls);
            f.Session.EnsureConnected(f.Amc);
            f.Session.SelectPortalProject(f.Amc, f.A.Id);
            Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
            Assert.Equal(before.PortalProcessId, f.Session.CurrentProcessId);

            f.A.Mode = TiaPortalMode.WithUserInterface;
            f.Session.Dispose();
            Assert.Equal(1, f.A.Portal.DisposeCalls);
            Assert.Null(f.Session.CurrentProcessId);
            f.Session.Dispose();
            Assert.Equal(1, f.A.Portal.DisposeCalls);
        }
        finally { f.A.Mode = TiaPortalMode.WithUserInterface; }
    }

    internal static void AssertCategory(string category, Action action)
        => Assert.Equal(category, Assert.Throws<WorkerOperationException>(action).FailureCategory);

    internal sealed class Fixture : IDisposable
    {
        private readonly TiaPortalProcess[] previous = TiaPortal.Processes.ToArray();
        private readonly string directory = Path.Combine(Path.GetTempPath(), "pr4-local-" + Guid.NewGuid().ToString("N"));
        public Session Session { get; } = new();
        public TiaPortalProcess A { get; }
        public string Amc { get; }
        public string OtherAmc { get; }
        public string Als { get; }
        public string OtherAls { get; }
        public string Ap { get; }
        public Fixture()
        {
            Directory.CreateDirectory(directory);
            TiaPortal.Processes.Clear();
            Amc = File("A.amc21"); OtherAmc = File("B.amc21");
            Als = File("A.als21"); OtherAls = File("B.als21"); Ap = File("A.ap21");
            A = Register(101, Amc);
        }
        public string File(string name)
        {
            var path = Path.Combine(directory, name);
            System.IO.File.WriteAllText(path, "fixture");
            return path;
        }
        public TiaPortalProcess Register(int id, string? advertisedPath)
        {
            var process = new TiaPortalProcess { Id = id, ProjectPath = advertisedPath is null ? null : new FileInfo(advertisedPath) };
            process.Portal = new TiaPortal { Process = process };
            TiaPortal.Processes.Add(process);
            return process;
        }
        public LocalSession AddLocal(TiaPortalProcess process, string? path)
        {
            var owner = new LocalSession { Project = new MultiuserProject { Path = path is null ? null : new FileInfo(path) } };
            process.Portal.LocalSessions.Items.Add(owner);
            return owner;
        }
        public PortalProject AddStandalone(TiaPortalProcess process, string path)
        {
            var project = new PortalProject { Path = new FileInfo(path), Calls = process.Portal.Projects.Calls };
            process.Portal.Projects.Items.Add(project);
            return project;
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
