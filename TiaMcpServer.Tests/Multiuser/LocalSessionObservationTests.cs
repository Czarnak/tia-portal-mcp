using Siemens.Engineering;
using Siemens.Engineering.Multiuser;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;
using static TiaMcpServer.Tests.Multiuser.LocalSessionSelectionWorkerTests;

namespace TiaMcpServer.Tests.Multiuser;

[Collection("Portal session boundary")]
public sealed class LocalSessionObservationTests
{
    [Fact]
    public void InitialOpen_DoesNotProveConnected()
    {
        using var f = new Fixture();
        f.A.Portal.LocalSessions.NextProjectPath = new FileInfo(f.Amc);
        f.Session.OpenProject(f.Als);
        var observation = f.Session.GetSessionIdentity().Context!.ConnectionObservation;
        Assert.NotNull(observation);
        Assert.Equal(ProjectServerConnectionStates.Unknown, observation.State);
        Assert.Equal(ProjectServerConnectionObservationSources.SessionOpen, observation.ObservationSource);
        Assert.Null(observation.PreviousState);
        Assert.False(observation.Transition);
        Assert.Null(f.Session.GetSessionIdentity().Context!.RemoteIdentity);
    }

    [Fact]
    public void RepeatedObservation_ReportsNoTransition()
    {
        var tracker = new ProjectServerObservationTracker();
        var identity = Endpoint();
        var first = tracker.Observe(identity, 1, ProjectServerConnectionStates.Connected,
            ProjectServerConnectionObservationSources.ExplicitRead);
        var second = tracker.Observe(identity, 1, ProjectServerConnectionStates.Connected,
            ProjectServerConnectionObservationSources.ExplicitRead);
        Assert.Null(first.PreviousState);
        Assert.Equal(ProjectServerConnectionStates.Connected, second.PreviousState);
        Assert.False(second.Transition);
        Assert.True(second.ObservedAt >= first.ObservedAt);
        Assert.Equal(ProjectServerConnectionObservationSources.ExplicitRead, second.ObservationSource);
    }

    [Fact]
    public void EndpointOrAttachmentChange_ResetsHistory()
    {
        var tracker = new ProjectServerObservationTracker();
        var identity = Endpoint();
        tracker.Observe(identity, 1, ProjectServerConnectionStates.Connected,
            ProjectServerConnectionObservationSources.ExplicitRead);
        Assert.Null(tracker.Observe(identity, 2, ProjectServerConnectionStates.Unknown,
            ProjectServerConnectionObservationSources.ExplicitRead).PreviousState);
        identity.Host = "changed.example";
        Assert.Null(tracker.Observe(identity, 2, ProjectServerConnectionStates.Connected,
            ProjectServerConnectionObservationSources.ExplicitRead).PreviousState);
        Assert.False(tracker.Observe(identity, 2, ProjectServerConnectionStates.Connected,
            ProjectServerConnectionObservationSources.ExplicitRead).Transition);
    }

    [Fact]
    public void ServerFailure_DoesNotUnbindLocalOwnerAndReconnectReportsTransition()
    {
        using var f = new Fixture();
        var owner = f.AddLocal(f.A, f.Amc);
        var server = new ProjectServer();
        f.A.Portal.ProjectServers.Items.Add(server);
        f.Session.Connect(f.Amc);
        var service = new MultiuserInventoryService(f.Session);
        var before = f.Session.GetSessionIdentity();
        service.ListServerGroups(new() { ServerAlias = "Fixture" });
        server.RemoteFailure = new EngineeringException("secret remote detail");
        var failure = Assert.Throws<TiaMcpServer.OpennessWorker.WorkerOperationException>(() =>
            service.ListServerGroups(new() { ServerAlias = "Fixture" }));
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, failure.FailureCategory);
        Assert.DoesNotContain("secret", failure.Message);
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        Assert.Null(f.Session.GetSessionIdentity().Context!.RemoteIdentity);
        Assert.Equal(ProjectServerConnectionStates.Unknown,
            f.Session.GetSessionIdentity().Context!.ConnectionObservation!.State);
        server.RemoteFailure = null;
        var reconnect = service.ListServerGroups(new() { ServerAlias = "Fixture" }).ConnectionObservation;
        Assert.Equal(ProjectServerConnectionStates.Unknown, reconnect.PreviousState);
        Assert.True(reconnect.Transition);
        Assert.Equal(0, owner.SaveCalls);
        Assert.Equal(0, owner.CloseCalls);
        Assert.Equal(0, owner.CommitCalls);
    }

    [Fact]
    public void UnrelatedInventoryAndDirectoryRow_DoNotJoinActiveOwner()
    {
        using var f = new Fixture();
        f.AddLocal(f.A, f.Amc);
        var server = new ProjectServer();
        server.Projects.Add(new ServerProjectInfo());
        server.Sessions.Add(new LocalSessionInfo
        {
            SessionId = 7,
            ProjectFileInfo = new FileInfo(Path.GetDirectoryName(f.Als)!)
        });
        f.A.Portal.ProjectServers.Items.Add(server);
        f.Session.Connect(f.Amc);
        var initial = f.Session.GetSessionIdentity().Context!.ConnectionObservation!;
        var service = new MultiuserInventoryService(f.Session);
        var result = service.ListLocalSessions(new()
        {
            ServerAlias = "Fixture",
            Group = new ProjectServerGroupIdentity { IsRoot = true },
            ServerProjectName = "Demo"
        });
        Assert.Single(result.Sessions);
        Assert.Equal(ProjectServerConnectionStates.Connected, result.ConnectionObservation.State);
        var active = f.Session.GetSessionIdentity().Context!;
        Assert.Null(active.RemoteIdentity);
        Assert.Equal(ProjectServerConnectionStates.Unknown, active.ConnectionObservation!.State);
        Assert.Equal(initial.ObservedAt, active.ConnectionObservation.ObservedAt);
        Assert.Equal(MultiuserSessionModes.Unknown, active.SessionMode);
    }

    private static MultiuserRemoteIdentity Endpoint() => new()
        { ServerAlias = "Fixture", Host = "server.example", Port = 8735 };

}
