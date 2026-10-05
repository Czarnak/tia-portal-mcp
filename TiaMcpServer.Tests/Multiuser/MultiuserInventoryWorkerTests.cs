using Siemens.Engineering;
using Siemens.Engineering.Multiuser;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;
using static TiaMcpServer.Tests.Multiuser.MultiuserPortalReadDispatchTests;

namespace TiaMcpServer.Tests.Multiuser;

[Collection("Portal session boundary")]
public sealed class MultiuserInventoryWorkerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SessionIds_AreProjectedAsReturnedIntegersWithoutInventedRangeRestriction(int sessionId)
    {
        using var f = new InventoryFixture();
        f.Server.Sessions.Add(new() { SessionId = sessionId, ProjectFileInfo = new FileInfo(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "A.als21")) });
        Assert.Equal(sessionId, Assert.Single(f.Service.ListLocalSessions(SessionsRequest()).Sessions).SessionId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingProjectName_IsValidationFailureBeforeRemoteRead(string? name)
    {
        using var f = new InventoryFixture();
        var request = SessionsRequest(); request.ServerProjectName = name!;
        Category("validation_error", () => f.Service.ListLocalSessions(request));
        Assert.Equal(0, f.Server.RemoteCalls);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("groups")]
    public void IncompleteConfigurationEnumeration_NeverReturnsPartialInventory(string method)
    {
        using var f = new InventoryFixture();
        f.Portal.Process.Portal.ProjectServers.EnumerationFailure = new EngineeringException("private endpoint diagnostic");
        Category("worker_operation_failed", () =>
        {
            if (method == "list") f.Service.ListServerConnections(new());
            else f.Service.ListServerGroups(new() { ServerAlias = "Fixture" });
        });
    }

    [Fact]
    public void UnrelatedEndpointObservation_IsNotOverwrittenByAnotherAlias()
    {
        using var f = new InventoryFixture();
        var other = new ProjectServer { ServerName = "Other" };
        f.Portal.Process.Portal.ProjectServers.Items.Add(other);
        f.Service.ListServerGroups(new() { ServerAlias = "Fixture" });
        other.RemoteFailure = new EngineeringException("failure");
        Category("worker_operation_failed", () => f.Service.ListServerGroups(new() { ServerAlias = "Other" }));
        var observation = f.Service.ListServerGroups(new() { ServerAlias = "Fixture" }).ConnectionObservation;
        Assert.Equal("connected", observation.PreviousState);
        Assert.False(observation.Transition);
    }

    private static ProjectServerGroupIdentity Root() => new() { IsRoot = true };
    private static MultiuserLocalSessionsRequest SessionsRequest(ProjectServerGroupIdentity? group = null) => new()
        { ServerAlias = "Fixture", Group = group ?? Root(), ServerProjectName = "Demo" };

    [Fact]
    public void ConfiguredConnections_AreCompleteWithoutRemoteCallsOrProjectAdoption()
    {
        using var f = new InventoryFixture();
        var result = f.Service.ListServerConnections(new());
        var row = Assert.Single(result.Connections);
        Assert.Equal("Fixture", row.ServerAlias);
        Assert.Equal("server.example", row.Host);
        Assert.Equal(8735, row.Port);
        Assert.Equal(0, f.Server.RemoteCalls);
        Assert.Null(f.Portal.Session.ActiveContext);
        f.Portal.AssertNoMutation();
    }

    [Fact]
    public void Groups_ReturnNamedIdentitiesWithConnectedObservationAndExplicitNullProtocol()
    {
        using var f = new InventoryFixture();
        f.Server.Groups.Add(new() { Name = "Exact" });
        var result = f.Service.ListServerGroups(new() { ServerAlias = "Fixture" });
        Assert.False(Assert.Single(result.Groups).IsRoot);
        Assert.Equal("Exact", result.Groups[0].Name);
        Assert.Equal("connected", result.ConnectionObservation.State);
        Assert.Equal("explicitRead", result.ConnectionObservation.ObservationSource);
        Assert.Null(result.ConnectionObservation.PreviousState);
        Assert.Null(result.RemoteIdentity.Protocol);
        Assert.Null(result.RemoteIdentity.Group);
    }

    [Fact]
    public void RootAndNamedGroup_KeepSameNamedProjectsSeparateAndUseActualRemoteHandle()
    {
        using var f = new InventoryFixture();
        var named = new ServerProjectInfo { ProjectName = "Demo" };
        f.Server.Groups.Add(new() { Name = "Exact", Projects = new List<ServerProjectInfo> { named } });
        var result = f.Service.ListLocalSessions(SessionsRequest(new() { Name = "Exact", IsRoot = false }));
        Assert.Same(named, f.Server.LastSessionProject);
        Assert.Equal("Exact", result.RemoteIdentity.Group!.Name);
        Assert.Equal("currentMachineCurrentUser", result.Scope);
        Assert.Empty(result.Sessions);
        f.Service.ListLocalSessions(SessionsRequest());
        Assert.Same(f.Server.Projects[0], f.Server.LastSessionProject);
        f.Portal.AssertNoMutation();
    }

    [Fact]
    public void Projects_ProjectActualNamesAndCompleteRemoteIdentity()
    {
        using var f = new InventoryFixture();
        var result = f.Service.ListServerProjects(new() { ServerAlias = "Fixture", Group = Root() });
        Assert.Equal("Demo", Assert.Single(result.Projects).Name);
        Assert.Equal("server.example", result.RemoteIdentity.Host);
        Assert.Equal(8735, result.RemoteIdentity.Port);
        Assert.True(result.RemoteIdentity.Group!.IsRoot);
        Assert.Null(result.RemoteIdentity.ServerProjectName);
    }

    [Theory]
    [InlineData("fixture", "target_not_found")]
    [InlineData(" Fixture", "target_not_found")]
    [InlineData("", "validation_error")]
    public void AliasResolution_IsExact(string alias, string category)
    {
        using var f = new InventoryFixture();
        Category(category, () => f.Service.ListServerGroups(new() { ServerAlias = alias }));
        Assert.Equal(0, f.Server.RemoteCalls);
    }

    [Fact]
    public void DuplicateAliases_RejectWithoutChoosingFirstServer()
    {
        using var f = new InventoryFixture();
        f.Portal.Process.Portal.ProjectServers.Items.Add(new ProjectServer());
        Category("target_ambiguous", () => f.Service.ListServerGroups(new() { ServerAlias = "Fixture" }));
        Assert.Equal(0, f.Server.RemoteCalls);
    }

    [Theory]
    [InlineData("missingGroup", "target_not_found")]
    [InlineData("duplicateGroup", "target_ambiguous")]
    [InlineData("missingProject", "target_not_found")]
    [InlineData("duplicateProject", "target_ambiguous")]
    [InlineData("wrongAlias", "target_evidence_mismatch")]
    [InlineData("blankProject", "worker_operation_failed")]
    public void RemoteResolution_FailsClosed(string fixture, string category)
    {
        using var f = new InventoryFixture();
        var request = SessionsRequest();
        switch (fixture)
        {
            case "missingGroup": request.Group = new() { Name = "Missing" }; break;
            case "duplicateGroup":
                request.Group = new() { Name = "Dup" };
                f.Server.Groups.Add(new() { Name = "Dup" }); f.Server.Groups.Add(new() { Name = "Dup" }); break;
            case "missingProject": request.ServerProjectName = "demo"; break;
            case "duplicateProject": f.Server.Projects.Add(new ServerProjectInfo()); break;
            case "wrongAlias": f.Server.Projects[0].ServerAlias = "other"; break;
            case "blankProject": f.Server.Projects[0].ProjectName = " "; break;
        }
        Category(category, () => f.Service.ListLocalSessions(request));
        Assert.Null(f.Server.LastSessionProject);
    }

    [Fact]
    public void MissingOrIncoherentGroup_IsRejectedBeforeRemoteCall()
    {
        using var f = new InventoryFixture();
        foreach (var group in new ProjectServerGroupIdentity?[] { null, new() { IsRoot = true, Name = "Root" }, new() { IsRoot = false } })
            Category("validation_error", () => f.Service.ListServerProjects(new() { ServerAlias = "Fixture", Group = group }));
        Assert.Equal(0, f.Server.RemoteCalls);
    }

    [Fact]
    public void Sessions_UseCanonicalAbsoluteFilePathAndSessionId()
    {
        using var f = new InventoryFixture();
        var file = new FileInfo(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sessions", "..", "A.als21"));
        f.Server.Sessions.Add(new() { SessionId = 7, ProjectFileInfo = file });
        var row = Assert.Single(f.Service.ListLocalSessions(SessionsRequest()).Sessions);
        Assert.Equal(7, row.SessionId);
        Assert.Equal(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "A.als21"), row.ProjectPath);
        Assert.Equal("Demo", f.Service.ListLocalSessions(SessionsRequest()).RemoteIdentity.ServerProjectName);
    }

    [Fact]
    public void IncompleteSessionRows_AreFailureNotEmptyOrPartialSuccess()
    {
        using var f = new InventoryFixture();
        f.Server.Sessions.Add(new() { SessionId = 1 });
        Category("worker_operation_failed", () => f.Service.ListLocalSessions(SessionsRequest()));
    }

    [Fact]
    public void UnlockedProject_DoesNotReadOwner()
    {
        using var f = new InventoryFixture();
        f.Server.Lock.OwnerFailure = new EngineeringException("owner must not be read");
        var result = f.Service.GetLockState(new() { ServerAlias = "Fixture", Group = Root(), ServerProjectName = "Demo" });
        Assert.False(result.IsLocked);
        Assert.Null(result.Owner);
        Assert.Equal(0, f.Server.Lock.OwnerReads);
        Assert.Same(f.Server.Projects[0], f.Server.LastLockProject);
        Assert.NotEqual(default, result.ObservedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LockedProject_RequiresOwnerAndStableSecondFlag(bool changed)
    {
        using var f = new InventoryFixture();
        f.Server.Lock.States.Enqueue(true); f.Server.Lock.States.Enqueue(!changed);
        var request = new MultiuserLockStateRequest { ServerAlias = "Fixture", Group = Root(), ServerProjectName = "Demo" };
        if (changed) Category("state_changed", () => f.Service.GetLockState(request));
        else
        {
            var result = f.Service.GetLockState(request);
            Assert.True(result.IsLocked); Assert.Equal("owner", result.Owner);
        }
        Assert.Equal(1, f.Server.Lock.OwnerReads);
    }

    [Fact]
    public void OrdinaryServerFailure_PreservesContextAndRecordsUnknownForNextObservation()
    {
        using var f = new InventoryFixture();
        f.Portal.Session.Connect(f.Portal.Path);
        var context = f.Portal.Session.ActiveContext;
        var before = f.Portal.Session.GetSessionIdentity();
        f.Service.ListServerGroups(new() { ServerAlias = "Fixture" });
        f.Server.RemoteFailure = new EngineeringException("secret remote diagnostic");
        var exception = Assert.Throws<TiaMcpServer.OpennessWorker.WorkerOperationException>(() => f.Service.ListServerGroups(new() { ServerAlias = "Fixture" }));
        Assert.Equal("worker_operation_failed", exception.FailureCategory);
        Assert.DoesNotContain("secret", exception.Message);
        Assert.Same(context, f.Portal.Session.ActiveContext);
        Assert.Equal(before.SessionGeneration, f.Portal.Session.GetSessionIdentity().SessionGeneration);
        f.Server.RemoteFailure = null;
        var observation = f.Service.ListServerGroups(new() { ServerAlias = "Fixture" }).ConnectionObservation;
        Assert.Equal("unknown", observation.PreviousState);
        Assert.Equal("connected", observation.State);
        Assert.True(observation.Transition);
        f.Portal.AssertNoMutation();
    }

    [Fact]
    public void ChangedEndpointOrAttachment_DoesNotCompareOldObservation()
    {
        using var f = new InventoryFixture();
        f.Service.ListServerGroups(new() { ServerAlias = "Fixture" });
        Assert.Equal("connected", f.Service.ListServerGroups(new() { ServerAlias = "Fixture" }).ConnectionObservation.PreviousState);
        f.Server.Host = "new.example";
        Assert.Null(f.Service.ListServerGroups(new() { ServerAlias = "Fixture" }).ConnectionObservation.PreviousState);
        f.Portal.Session.Disconnect();
        Assert.Null(f.Service.ListServerGroups(new() { ServerAlias = "Fixture" }).ConnectionObservation.PreviousState);
    }

    private sealed class InventoryFixture : IDisposable
    {
        public PortalInventoryFixture Portal { get; } = new();
        public ProjectServer Server { get; } = new();
        public MultiuserInventoryService Service { get; }
        public InventoryFixture()
        {
            Portal.Process.Portal.ProjectServers.Items.Add(Server);
            Server.Projects.Add(new());
            Service = new(Portal.Session);
        }
        public void Dispose() => Portal.Dispose();
    }
}
