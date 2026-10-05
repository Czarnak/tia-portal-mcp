using Xunit;
using Siemens.Engineering;
using Siemens.Engineering.Multiuser;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness;

namespace TiaMcpServer.Tests.Worker;

public class ActiveProjectContextTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneContext_PreservesRootAndOwner(bool owned)
    {
        var project = new Siemens.Engineering.Project();
        var context = ActiveProjectContext.ForStandalone(project, owned);
        Assert.Same(project, context.EngineeringRoot);
        Assert.Same(project, Assert.IsType<StandaloneProjectOwner>(context.Owner).Project);
        Assert.Equal(owned, context.Owner.OpenedByWorker);
        Assert.Equal(ProjectContainerKinds.StandaloneProject, context.ContainerKind);
        Assert.Equal(MultiuserSessionModes.NotApplicable, context.SessionMode);
        AssertPassive(context);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalContext_PreservesSessionOwnerWithoutMutation(bool owned)
    {
        var session = new LocalSession();
        var context = ActiveProjectContext.ForLocalSession(session, owned,
            ProjectContainerKinds.LocalSession, MultiuserSessionModes.Unknown);
        var owner = Assert.IsType<LocalSessionOwner>(context.Owner);
        Assert.Same(session, owner.LocalSession);
        Assert.Same(session.Project, owner.Project);
        Assert.Same(session.Project, context.EngineeringRoot);
        Assert.Equal(owned, owner.OpenedByWorker);
        Assert.Equal(ProjectContainerKinds.LocalSession, context.ContainerKind);
        Assert.Equal(MultiuserSessionModes.Unknown, context.SessionMode);
        AssertPassive(context);
        AssertNoMutation(session);
    }

    [Theory]
    [InlineData(ProjectContainerKinds.ServerProject, MultiuserSessionModes.Multiuser)]
    [InlineData(ProjectContainerKinds.ServerProject, MultiuserSessionModes.Exclusive)]
    [InlineData(ProjectContainerKinds.ServerProject, MultiuserSessionModes.Unknown)]
    [InlineData(ProjectContainerKinds.LocalSession, MultiuserSessionModes.Multiuser)]
    [InlineData(ProjectContainerKinds.LocalSession, MultiuserSessionModes.Exclusive)]
    public void ServerContext_RequiresLocalOwner(string kind, string mode)
    {
        var session = new LocalSession();
        var context = ActiveProjectContext.ForLocalSession(session, false, kind, mode);
        Assert.IsType<LocalSessionOwner>(context.Owner);
        Assert.Equal(kind, context.ContainerKind);
        Assert.Equal(mode, context.SessionMode);
        AssertPassive(context);
        AssertNoMutation(session);
    }

    [Fact]
    public void NullOwnerOrRoot_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => ActiveProjectContext.ForStandalone(null!, false));
        Assert.Throws<ArgumentNullException>(() => ActiveProjectContext.ForLocalSession(null!, false,
            ProjectContainerKinds.LocalSession, MultiuserSessionModes.Unknown));
        var session = new LocalSession { Project = null! };
        Assert.Throws<ArgumentException>(() => ActiveProjectContext.ForLocalSession(session, false,
            ProjectContainerKinds.LocalSession, MultiuserSessionModes.Unknown));
        AssertNoMutation(session);
    }

    [Theory]
    [InlineData(ProjectContainerKinds.StandaloneProject, MultiuserSessionModes.Unknown)]
    [InlineData(ProjectContainerKinds.LocalSession, MultiuserSessionModes.NotApplicable)]
    [InlineData(ProjectContainerKinds.ServerProject, "invalid")]
    [InlineData("invalid", MultiuserSessionModes.Unknown)]
    [InlineData(null, MultiuserSessionModes.Unknown)]
    [InlineData(ProjectContainerKinds.LocalSession, null)]
    public void InvalidContainerOrMode_IsRejectedWithoutMutation(string? kind, string? mode)
    {
        var session = new LocalSession();
        Assert.Throws<ArgumentException>(() => ActiveProjectContext.ForLocalSession(session, true, kind!, mode!));
        AssertNoMutation(session);
    }

    [Theory]
    [InlineData("Example_LS.als21")]
    [InlineData("Example_ES.als21")]
    public void Filename_DoesNotInferUnknownMode(string filename)
    {
        var session = new LocalSession { Project = new MultiuserProject { Path = new FileInfo(filename) } };
        var context = ActiveProjectContext.ForLocalSession(session, false,
            ProjectContainerKinds.LocalSession, MultiuserSessionModes.Unknown);
        Assert.Equal(MultiuserSessionModes.Unknown, context.SessionMode);
        AssertNoMutation(session);
    }

    private static void AssertPassive(ActiveProjectContext context)
    {
        Assert.Empty(context.CapabilitySet);
        Assert.Null(context.RemoteIdentity);
        Assert.Null(context.ConnectionObservation);
    }

    private static void AssertNoMutation(LocalSession session)
    {
        Assert.Equal(0, session.SaveCalls);
        Assert.Equal(0, session.CloseCalls);
        Assert.Equal(0, session.CommitCalls);
    }
}
