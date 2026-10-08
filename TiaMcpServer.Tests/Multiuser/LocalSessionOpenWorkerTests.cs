using Siemens.Engineering;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;
using Fixture = TiaMcpServer.Tests.Multiuser.LocalSessionSelectionWorkerTests.Fixture;

namespace TiaMcpServer.Tests.Multiuser;

[Collection("Portal session boundary")]
public sealed class LocalSessionOpenWorkerTests
{
    [Theory]
    [InlineData("empty")]
    [InlineData("standalone")]
    [InlineData("local")]
    [InlineData("mixed")]
    [InlineData("multiple")]
    [InlineData("unreadable")]
    public void EmptyPortalValidation_RequiresZeroOwnersWithoutSelecting(string owners)
    {
        using var f = new Fixture();
        f.Session.EnsurePortalConnected(f.A.Id);
        var empty = f.Session.GetSessionIdentity();
        if (owners is "standalone" or "mixed") f.AddStandalone(f.A, f.Ap);
        if (owners is "local" or "mixed" or "multiple" or "unreadable") f.AddLocal(f.A, f.Amc);
        if (owners == "multiple") f.AddLocal(f.A, f.OtherAmc);
        if (owners == "unreadable") f.A.Portal.LocalSessions.Items[0].Project.PathFailure = new EngineeringException("unreadable owner");

        if (owners == "empty") f.Session.ValidateEmptyPortal(empty);
        else LocalSessionSelectionWorkerTests.AssertCategory(owners == "unreadable"
            ? WorkerFailureCategories.PostconditionFailed : WorkerFailureCategories.BindingConflict,
            () => f.Session.ValidateEmptyPortal(empty));

        Assert.Null(f.Session.ActiveContext);
        Assert.Equal(empty.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        Assert.Equal(1, f.A.AttachCalls);
        Assert.Equal(0, f.A.Portal.DisposeCalls);
        Assert.Equal(0, f.A.Portal.Projects.OpenCalls + f.A.Portal.LocalSessions.OpenCalls);
        Assert.All(f.A.Portal.Projects.Items, project => Assert.Equal(0, project.SaveCalls + project.CloseCalls));
        Assert.All(f.A.Portal.LocalSessions.Items, owner => Assert.Equal(0, owner.SaveCalls + owner.CloseCalls + owner.CommitCalls));
    }

    [Theory]
    [InlineData("worker")]
    [InlineData("portal")]
    [InlineData("generation")]
    [InlineData("context")]
    [InlineData("path")]
    [InlineData("disconnected")]
    public void EmptyPortalValidation_RejectsIncompleteOrChangedAttachment(string fault)
    {
        using var f = new Fixture();
        f.Session.EnsurePortalConnected(f.A.Id);
        var expected = f.Session.GetSessionIdentity();
        switch (fault)
        {
            case "worker": expected.WorkerSessionId = "different-worker"; break;
            case "portal": expected.PortalProcessId = 999; break;
            case "generation": expected.SessionGeneration++; break;
            case "context": expected.Context = new ProjectContextInfo(); break;
            case "path": expected.ProjectPath = f.Amc; break;
            case "disconnected": f.Session.Disconnect(); break;
        }

        LocalSessionSelectionWorkerTests.AssertCategory(WorkerFailureCategories.BindingConflict,
            () => f.Session.ValidateEmptyPortal(expected));
        Assert.Null(f.Session.ActiveContext);
        Assert.Equal(1, f.A.AttachCalls);
        Assert.Equal(0, f.A.Portal.Projects.OpenCalls + f.A.Portal.LocalSessions.OpenCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyPortalValidation_AttachmentDriftDuringEnumerationIsRejected(bool disposed)
    {
        using var f = new Fixture();
        f.Session.EnsurePortalConnected(f.A.Id);
        var empty = f.Session.GetSessionIdentity();
        f.A.Portal.LocalSessions.OnEnumerate = _ =>
        {
            if (disposed) f.A.Portal.RaiseDisposed();
            else f.A.Id = 999;
        };

        LocalSessionSelectionWorkerTests.AssertCategory(WorkerFailureCategories.BindingConflict,
            () => f.Session.ValidateEmptyPortal(empty));
        Assert.Null(f.Session.ActiveContext);
        Assert.Equal(1, f.A.AttachCalls);
        Assert.Equal(0, f.A.Portal.Projects.OpenCalls + f.A.Portal.LocalSessions.OpenCalls);
    }

    [Fact]
    public void EmptyPortalValidation_UnreadableEnumerationIsNotTreatedAsEmpty()
    {
        using var f = new Fixture();
        f.Session.EnsurePortalConnected(f.A.Id);
        var empty = f.Session.GetSessionIdentity();
        f.A.Portal.LocalSessions.OnEnumerate = _ => throw new EngineeringException("unreadable collection");

        Assert.Throws<EngineeringException>(() => f.Session.ValidateEmptyPortal(empty));
        Assert.Null(f.Session.ActiveContext);
        Assert.Equal(1, f.A.AttachCalls);
        Assert.Equal(0, f.A.Portal.Projects.OpenCalls + f.A.Portal.LocalSessions.OpenCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyPortalValidation_OwnerAfterProofCannotReachAlsOpener(bool local)
    {
        using var f = new Fixture();
        f.Session.EnsurePortalConnected(f.A.Id);
        var empty = f.Session.GetSessionIdentity();
        f.Session.ValidateEmptyPortal(empty);
        if (local) f.AddLocal(f.A, f.OtherAmc);
        else f.AddStandalone(f.A, f.Ap);
        // An unselected owner does not change the selected identity; the opener must enumerate it.
        f.Session.ValidateExpectedSessionIdentity(empty, false);

        LocalSessionSelectionWorkerTests.AssertCategory(WorkerFailureCategories.GuardBlocked,
            () => f.Session.OpenProject(f.Als));

        Assert.Null(f.Session.ActiveContext);
        Assert.Equal(1, f.A.AttachCalls);
        Assert.Equal(0, f.A.Portal.DisposeCalls);
        Assert.Equal(0, f.A.Portal.Projects.OpenCalls + f.A.Portal.LocalSessions.OpenCalls);
        Assert.All(f.A.Portal.Projects.Items, project => Assert.Equal(0, project.SaveCalls + project.CloseCalls));
        Assert.All(f.A.Portal.LocalSessions.Items, owner => Assert.Equal(0, owner.SaveCalls + owner.CloseCalls + owner.CommitCalls));
    }

    [Fact]
    public void AlsOpen_UsesOnlyLocalSessionsOpen()
    {
        using var f = new Fixture();
        f.A.Portal.LocalSessions.NextProjectPath = new FileInfo(f.Amc);
        f.Session.OpenProject(f.Als);
        Assert.Equal(1, f.A.Portal.LocalSessions.OpenCalls);
        Assert.Equal(f.Als, f.A.Portal.LocalSessions.LastOpenedFile!.FullName);
        Assert.Equal(0, f.A.Portal.Projects.OpenCalls);
        Assert.Equal(f.Amc, f.Session.GetSessionIdentity().ProjectPath);
        Assert.Equal(f.Als, ContainerPath(f.Session.ActiveContext!));
        Assert.True(f.Session.ActiveContext!.Owner.OpenedByWorker);
    }

    [Fact]
    public void ApOpen_UsesOnlyProjectsOpen()
    {
        using var f = new Fixture();
        f.Session.OpenProject(f.Ap);
        Assert.Equal(1, f.A.Portal.Projects.OpenCalls);
        Assert.Equal(0, f.A.Portal.LocalSessions.OpenCalls);
        Assert.Equal(f.Ap, f.Session.GetSessionIdentity().ProjectPath);
    }

    [Fact]
    public void ProvedAlsDestinationReuse_DoesNotOpenAgain()
    {
        using var f = new Fixture();
        f.A.Portal.LocalSessions.NextProjectPath = new FileInfo(f.Amc);
        f.Session.OpenProject(f.Als);
        var before = f.Session.GetSessionIdentity();
        f.Session.OpenProject(f.Als);
        Assert.Equal(1, f.A.Portal.LocalSessions.OpenCalls);
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        Assert.True(f.Session.ActiveContext!.Owner.OpenedByWorker);
    }

    [Fact]
    public void ColdOwner_UnknownAlsDestination_BlocksBeforeOpener()
    {
        using var f = new Fixture();
        f.AddLocal(f.A, f.Amc);
        f.Session.Connect(f.Amc);
        LocalSessionSelectionWorkerTests.AssertCategory(WorkerFailureCategories.GuardBlocked,
            () => f.Session.OpenProject(f.Als));
        Assert.Equal(0, f.A.Portal.LocalSessions.OpenCalls);
        Assert.Null(ContainerPath(f.Session.ActiveContext!));
    }

    [Fact]
    public void WorkerOwnedLocalSource_BlocksDifferentOpenEvenWhenClean()
    {
        using var f = new Fixture();
        f.A.Portal.LocalSessions.NextProjectPath = new FileInfo(f.Amc);
        f.Session.OpenProject(f.Als);
        var owner = Assert.Single(f.A.Portal.LocalSessions.Items);
        var before = f.Session.GetSessionIdentity();
        LocalSessionSelectionWorkerTests.AssertCategory(WorkerFailureCategories.GuardBlocked,
            () => f.Session.OpenProject(f.OtherAls));
        Assert.Equal(1, f.A.Portal.LocalSessions.OpenCalls);
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        Assert.Equal(0, owner.SaveCalls + owner.CloseCalls + owner.CommitCalls);
    }

    [Fact]
    public void BorrowedStandalone_BlocksUnprovedAlsCoexistenceBeforeOpener()
    {
        using var f = new Fixture();
        var project = f.AddStandalone(f.A, f.Ap);
        f.Session.Connect(f.Ap);
        LocalSessionSelectionWorkerTests.AssertCategory(WorkerFailureCategories.GuardBlocked,
            () => f.Session.OpenProject(f.Als));
        Assert.Equal(0, f.A.Portal.LocalSessions.OpenCalls);
        Assert.Equal(0, project.SaveCalls + project.CloseCalls);
        Assert.Equal(f.Ap, f.Session.GetSessionIdentity().ProjectPath);
    }

    [Fact]
    public void DisposeNeverCallsLocalSaveCloseCommit()
    {
        using var f = new Fixture();
        f.A.Portal.LocalSessions.NextProjectPath = new FileInfo(f.Amc);
        f.Session.OpenProject(f.Als);
        var owner = Assert.Single(f.A.Portal.LocalSessions.Items);
        f.Session.Dispose();
        Assert.Equal(0, owner.SaveCalls + owner.CloseCalls + owner.CommitCalls);
    }

    [Fact]
    public void PostOpenInvalidOwner_RetainsPossibleMutationWithoutCleanup()
    {
        using var f = new Fixture();
        LocalSessionSelectionWorkerTests.AssertCategory(WorkerFailureCategories.PostconditionFailed,
            () => f.Session.OpenProject(f.Als));
        var returnedOwner = Assert.Single(f.A.Portal.LocalSessions.Items);
        Assert.Equal(1, f.A.Portal.LocalSessions.OpenCalls);
        Assert.Equal(0, returnedOwner.SaveCalls + returnedOwner.CloseCalls + returnedOwner.CommitCalls);
        Assert.Null(f.Session.ActiveContext);
    }

    [Fact]
    public void OwnerAppearingBeforeAlsDispatch_BlocksWithoutOpener()
    {
        using var f = new Fixture();
        f.A.Portal.LocalSessions.NextProjectPath = new FileInfo(f.Amc);
        f.A.Portal.LocalSessions.OnEnumerate = count =>
        {
            if (count == 3) f.AddLocal(f.A, f.OtherAmc);
        };
        LocalSessionSelectionWorkerTests.AssertCategory(WorkerFailureCategories.GuardBlocked,
            () => f.Session.OpenProject(f.Als));
        Assert.Equal(0, f.A.Portal.LocalSessions.OpenCalls);
        Assert.Null(f.Session.ActiveContext);
    }

    [Fact]
    public void DetachedStandaloneSource_BlocksAlsBeforeCloseOrOpen()
    {
        using var f = new Fixture();
        f.Session.OpenProject(f.Ap);
        var source = Assert.Single(f.A.Portal.Projects.Items);
        f.A.Portal.Projects.Items.Clear();
        f.A.Portal.LocalSessions.NextProjectPath = new FileInfo(f.Amc);
        LocalSessionSelectionWorkerTests.AssertCategory(WorkerFailureCategories.BindingConflict,
            () => f.Session.OpenProject(f.Als));
        Assert.Equal(0, source.CloseCalls);
        Assert.Equal(0, f.A.Portal.LocalSessions.OpenCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedAlsReplacement_ClosesSourceWithoutCleanupOrReplay(bool openerThrows)
    {
        using var f = new Fixture();
        f.Session.OpenProject(f.Ap);
        var source = Assert.Single(f.A.Portal.Projects.Items);
        if (openerThrows) f.A.Portal.LocalSessions.OpenFailure = new InvalidOperationException("opener failed");

        if (openerThrows)
            Assert.Equal("opener failed", Assert.Throws<InvalidOperationException>(() => f.Session.OpenProject(f.Als)).Message);
        else
            LocalSessionSelectionWorkerTests.AssertCategory(WorkerFailureCategories.PostconditionFailed,
                () => f.Session.OpenProject(f.Als));

        Assert.Equal(1, source.CloseCalls);
        Assert.Equal(0, source.SaveCalls);
        Assert.Empty(f.A.Portal.Projects.Items);
        Assert.Equal(1, f.A.Portal.LocalSessions.OpenCalls);
        Assert.Null(f.Session.ActiveContext);
        foreach (var owner in f.A.Portal.LocalSessions.Items)
            Assert.Equal(0, owner.SaveCalls + owner.CloseCalls + owner.CommitCalls);
    }

    private static string? ContainerPath(object context)
        => context.GetType().GetProperty("SessionContainerPath")?.GetValue(context) as string;
}
