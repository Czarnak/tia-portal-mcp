using Siemens.Engineering;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;
using Fixture = TiaMcpServer.Tests.Multiuser.LocalSessionSelectionWorkerTests.Fixture;

namespace TiaMcpServer.Tests.Multiuser;

[Collection("Portal session boundary")]
public sealed class LocalSessionOpenWorkerTests
{
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

    private static string? ContainerPath(object context)
        => context.GetType().GetProperty("SessionContainerPath")?.GetValue(context) as string;
}
