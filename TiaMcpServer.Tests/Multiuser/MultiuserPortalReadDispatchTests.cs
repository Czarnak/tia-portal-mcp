using Siemens.Engineering;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;
using Session = TiaMcpServer.OpennessWorker.Openness.TiaPortalSession;
using PortalProject = Siemens.Engineering.Project;

namespace TiaMcpServer.Tests.Multiuser;

[Collection("Portal session boundary")]
public sealed class MultiuserPortalReadDispatchTests
{
    [Theory]
    [InlineData("list_server_connections")]
    [InlineData("list_server_groups")]
    [InlineData("list_server_projects")]
    [InlineData("list_local_sessions")]
    [InlineData("get_lock_state")]
    public void InventoryDispatch_ReturnsTypedPayloadAndActualPidInEveryAccessMode(string method)
    {
        foreach (var mode in new[] { McpAccessMode.ReadOnly, McpAccessMode.ReadWrite, McpAccessMode.Full })
        {
            using var f = new PortalInventoryFixture();
            Assert.Null(WorkerOperationAuthorization.Authorize(mode, method));
            var server = new Siemens.Engineering.Multiuser.ProjectServer();
            server.Projects.Add(new Siemens.Engineering.Multiuser.ServerProjectInfo());
            f.Process.Portal.ProjectServers.Items.Add(server);
            var service = new MultiuserInventoryService(f.Session);
            var request = new WorkerRequest { Method = method };
            if (method != "list_server_connections") request.MultiuserServerAlias = "Fixture";
            if (method is "list_server_projects" or "list_local_sessions" or "get_lock_state") request.MultiuserGroupIsRoot = true;
            if (method is "list_local_sessions" or "get_lock_state") request.MultiuserServerProjectName = "Demo";
            var result = MultiuserPortalReadDispatch.Run(f.Session, request, _ => MultiuserPortalReadDispatch.Invoke(service, request));
            Assert.True(result.Success);
            Assert.Equal(101, result.PortalProcessId);
            using var document = System.Text.Json.JsonDocument.Parse(result.Payload!);
            var slot = method switch { "list_server_connections" => "connections", "list_server_groups" => "groups",
                "list_server_projects" => "projects", "list_local_sessions" => "sessions", _ => "isLocked" };
            Assert.True(document.RootElement.TryGetProperty(slot, out _));
            if (method != "list_server_connections") Assert.Equal(System.Text.Json.JsonValueKind.Null,
                document.RootElement.GetProperty("remoteIdentity").GetProperty("protocol").ValueKind);
            Assert.Null(f.Session.ActiveContext);
            f.AssertNoMutation();
        }
    }

    [Fact]
    public void BodyFailure_WithActualPortalLossStillInvalidatesIdentity()
    {
        using var f = new PortalInventoryFixture();
        f.Session.Connect(f.Path);
        var request = new WorkerRequest { Method = "list_server_connections", ExpectedSessionIdentity = f.Session.GetSessionIdentity() };
        Category("binding_conflict", () => MultiuserPortalReadDispatch.Run(f.Session, request, _ =>
        {
            f.Process.Portal.CurrentProcessFailure = new NonRecoverableException("closed");
            throw new WorkerOperationException("worker_operation_failed", "server read failed");
        }));
        Assert.Null(f.Session.CurrentProcessId);
        Assert.Null(f.Session.ActiveContext);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposedDuringRemoteRead_DoesNotReattachOrReplay(bool bodyFails)
    {
        using var f = new PortalInventoryFixture();
        f.Session.Connect(f.Path);
        var request = new WorkerRequest { Method = "list_server_connections", ExpectedSessionIdentity = f.Session.GetSessionIdentity() };
        var bodyCalls = 0;
        Category("binding_conflict", () => MultiuserPortalReadDispatch.Run(f.Session, request, _ =>
        {
            bodyCalls++;
            f.Process.Portal.RaiseDisposed();
            if (bodyFails) throw new WorkerOperationException("worker_operation_failed", "lost");
            return new WorkerResponse { Success = true, Payload = "{}" };
        }));
        Assert.Equal(1, bodyCalls);
        Assert.Equal(1, f.Process.AttachCalls);
        Assert.Null(f.Session.CurrentProcessId);
    }

    [Fact]
    public void HealthyBodyFailure_PreservesBindingOwnershipAndOriginalCategory()
    {
        using var f = new PortalInventoryFixture();
        f.Session.Connect(f.Path);
        f.Session.TrackWorkerOpenedProject(f.Project);
        var before = f.Session.GetSessionIdentity();
        var context = f.Session.ActiveContext;
        var request = new WorkerRequest { Method = "list_server_connections", ExpectedSessionIdentity = before };
        Category("target_not_found", () => MultiuserPortalReadDispatch.Run(f.Session, request, _ =>
            throw new WorkerOperationException("target_not_found", "no server")));
        Assert.Same(context, f.Session.ActiveContext);
        Assert.True(context!.Owner.OpenedByWorker);
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        f.AssertNoMutation();
    }

    [Fact]
    public void SuccessfulBody_CannotCertifyIdentityLostDuringRemoteRead()
    {
        using var f = new PortalInventoryFixture();
        f.Session.Connect(f.Path);
        var request = new WorkerRequest { Method = "list_server_connections", ExpectedSessionIdentity = f.Session.GetSessionIdentity() };
        Category("binding_conflict", () => MultiuserPortalReadDispatch.Run(f.Session, request, _ =>
        {
            f.Project.PathFailure = new EngineeringException("closed");
            return new WorkerResponse { Success = true, Payload = "{}" };
        }));
        Assert.Null(f.Session.ActiveContext);
    }

    [Fact]
    public void FirstAttachment_DoesNotAdoptSoleOpenProject()
    {
        using var f = new PortalInventoryFixture();
        f.Session.EnsurePortalConnected();
        Assert.Equal(101, f.Session.CurrentProcessId);
        Assert.Null(f.Session.ActiveContext);
        Assert.Null(f.Session.GetSessionIdentity().ProjectPath);
        Assert.Equal(1, f.Process.AttachCalls);
        f.AssertNoMutation();
    }

    [Fact]
    public void SamePidReuse_PreservesWorkerOwnershipAndIdentity()
    {
        using var f = new PortalInventoryFixture();
        f.Session.Connect(f.Path);
        f.Session.TrackWorkerOpenedProject(f.Project);
        var context = f.Session.ActiveContext;
        var before = f.Session.GetSessionIdentity();
        f.Session.EnsurePortalConnected(101);
        Assert.Same(context, f.Session.ActiveContext);
        Assert.True(context!.Owner.OpenedByWorker);
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        Assert.Equal(1, f.Process.AttachCalls);
        f.AssertNoMutation();
    }

    [Fact]
    public void ForeignPidRefusal_IsBeforePortalReadAndPreservesContext()
    {
        using var f = new PortalInventoryFixture();
        f.Session.Connect(f.Path);
        var before = f.Session.GetSessionIdentity();
        var context = f.Session.ActiveContext;
        f.Process.Portal.CurrentProcessFailure = new EngineeringException("should not read");
        Category(WorkerFailureCategories.BindingConflict, () => f.Session.EnsurePortalConnected(202));
        Assert.Same(context, f.Session.ActiveContext);
        f.Process.Portal.CurrentProcessFailure = null;
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
        Assert.Equal(0, f.Process.Portal.DisposeCalls);
        f.AssertNoMutation();
    }

    [Theory]
    [InlineData(0, null, "target_not_found")]
    [InlineData(2, null, "target_ambiguous")]
    [InlineData(1, 999, "target_not_found")]
    public void UnattachedSelection_RejectsAbsentOrAmbiguousCandidates(int count, int? pid, string category)
    {
        using var f = new PortalInventoryFixture();
        TiaPortal.Processes.Clear();
        for (var i = 0; i < count; i++) f.Register(101 + i);
        Category(category, () => f.Session.EnsurePortalConnected(pid));
        Assert.False(f.Session.IsConnected);
        Assert.All(TiaPortal.Processes, p => Assert.Equal(0, p.AttachCalls));
    }

    [Fact]
    public void ExplicitPid_SelectsOnlyExactPortal()
    {
        using var f = new PortalInventoryFixture();
        var second = f.Register(202);
        f.Session.EnsurePortalConnected(202);
        Assert.Equal(202, f.Session.CurrentProcessId);
        Assert.Equal(0, f.Process.AttachCalls);
        Assert.Equal(1, second.AttachCalls);
        Assert.Null(f.Session.ActiveContext);
    }

    [Fact]
    public void CachedIdentityRefusal_PrecedesPortalActivity()
    {
        using var f = new PortalInventoryFixture();
        var request = new WorkerRequest { Method = "list_server_connections",
            ExpectedSessionIdentity = new WorkerSessionIdentity { WorkerSessionId = "foreign" } };
        Category(WorkerFailureCategories.BindingConflict, () => Run(f, request));
        Assert.Equal(0, f.Process.AttachCalls);
    }

    [Fact]
    public void LiveIdentityRefusal_DoesNotAdoptReplacementProject()
    {
        using var f = new PortalInventoryFixture();
        f.Session.Connect(f.Path);
        var request = new WorkerRequest { Method = "list_server_connections", ExpectedSessionIdentity = f.Session.GetSessionIdentity() };
        f.Project.PathFailure = new EngineeringException("closed");
        Category(WorkerFailureCategories.BindingConflict, () => Run(f, request));
        Assert.Null(f.Session.ActiveContext);
        f.AssertNoMutation();
    }

    [Fact]
    public void ActualPortalLoss_InvalidatesAttachedIdentity()
    {
        using var f = new PortalInventoryFixture();
        f.Session.Connect(f.Path);
        var before = f.Session.GetSessionIdentity();
        f.Process.Portal.CurrentProcessFailure = new NonRecoverableException("closed");
        Category(WorkerFailureCategories.BindingConflict, () => f.Session.EnsurePortalConnected());
        Assert.Null(f.Session.CurrentProcessId);
        Assert.Null(f.Session.ActiveContext);
        Assert.True(f.Session.GetSessionIdentity().SessionGeneration > before.SessionGeneration);
    }

    [Theory]
    [InlineData("projectPath")]
    [InlineData("confirm")]
    [InlineData("forceRebind")]
    [InlineData("alias")]
    [InlineData("group")]
    [InlineData("project")]
    [InlineData("pid")]
    public void DirectWorkerEnvelope_RejectsInappropriateSelectorsBeforeAttachment(string invalid)
    {
        using var f = new PortalInventoryFixture();
        var request = new WorkerRequest { Method = "list_server_connections" };
        switch (invalid)
        {
            case "projectPath": request.ProjectPath = f.Path; break;
            case "confirm": request.Confirm = true; break;
            case "forceRebind": request.ForceRebind = true; break;
            case "alias": request.MultiuserServerAlias = "Fixture"; break;
            case "group": request.MultiuserGroupIsRoot = true; break;
            case "project": request.MultiuserServerProjectName = "Demo"; break;
            case "pid": request.PortalProcessId = 0; break;
        }
        Category(WorkerFailureCategories.ValidationError, () => Run(f, request));
        Assert.Equal(0, f.Process.AttachCalls);
        f.AssertNoMutation();
    }

    private static void Run(PortalInventoryFixture f, WorkerRequest request)
        => MultiuserPortalReadDispatch.Run(f.Session, request, _ => throw new Xunit.Sdk.XunitException("body must not run"));
    internal static void Category(string expected, Action action)
        => Assert.Equal(expected, Assert.Throws<WorkerOperationException>(action).FailureCategory);
}

internal sealed class PortalInventoryFixture : IDisposable
{
    private readonly TiaPortalProcess[] previous = TiaPortal.Processes.ToArray();
    public Session Session { get; } = new();
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "inventory.ap21");
    public TiaPortalProcess Process { get; }
    public PortalProject Project => Process.Portal.Projects.Items[0];
    public PortalInventoryFixture() { TiaPortal.Processes.Clear(); Process = Register(101); }
    public TiaPortalProcess Register(int id)
    {
        var p = new TiaPortalProcess { Id = id, ProjectPath = new FileInfo(Path) };
        p.Portal = new TiaPortal { Process = p };
        p.Portal.Projects.Items.Add(new PortalProject { Path = new FileInfo(Path) });
        TiaPortal.Processes.Add(p);
        return p;
    }
    public void AssertNoMutation()
    {
        Assert.Equal(0, Process.Portal.Projects.OpenCalls);
        Assert.Equal(0, Project.SaveCalls);
        Assert.Equal(0, Project.CloseCalls);
    }
    public void Dispose()
    {
        Session.Dispose();
        TiaPortal.Processes.Clear(); TiaPortal.Processes.AddRange(previous);
    }
}
