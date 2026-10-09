using Siemens.Engineering.Multiuser;
using TiaMcpServer.Contracts.Multiuser;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Multiuser;

/// <summary>Portal-only remote inventory. No local session/project is ever opened or adopted.</summary>
internal sealed class MultiuserInventoryService
{
    private readonly TiaPortalSession _session;
    private readonly ProjectServerObservationTracker _observations = new();

    public MultiuserInventoryService(TiaPortalSession session) => _session = session;

    public MultiuserServerConnectionsInfo ListServerConnections(MultiuserServerConnectionsRequest request)
    {
        Connect(request.PortalProcessId);
        return new MultiuserServerConnectionsInfo
        {
            Connections = Connections().Select(row => new MultiuserServerConnectionInfo
                { ServerAlias = row.Identity.ServerAlias, Host = row.Identity.Host!, Port = row.Identity.Port!.Value }).ToList()
        };
    }

    public MultiuserServerGroupsInfo ListServerGroups(MultiuserServerGroupsRequest request)
    {
        RequireName(request.ServerAlias);
        Connect(request.PortalProcessId);
        var target = Server(request.ServerAlias);
        var result = new MultiuserServerGroupsInfo { RemoteIdentity = target.Identity };
        result.ConnectionObservation = Read(target, () =>
            result.Groups = Groups(target.Server).Select(group => new ProjectServerGroupIdentity { IsRoot = false, Name = group.Name }).ToList());
        return result;
    }

    public MultiuserServerProjectsInfo ListServerProjects(MultiuserServerProjectsRequest request)
    {
        Validate(request.ServerAlias, request.Group);
        Connect(request.PortalProcessId);
        var target = Server(request.ServerAlias);
        target.Identity.Group = Copy(request.Group!);
        var result = new MultiuserServerProjectsInfo { RemoteIdentity = target.Identity };
        result.ConnectionObservation = Read(target, () =>
            result.Projects = Projects(target.Server, request.Group!, request.ServerAlias)
                .Select(project => new MultiuserServerProjectInfo { Name = project.ProjectName }).ToList());
        return result;
    }

    public MultiuserLocalSessionsInfo ListLocalSessions(MultiuserLocalSessionsRequest request)
    {
        Validate(request.ServerAlias, request.Group);
        RequireName(request.ServerProjectName);
        Connect(request.PortalProcessId);
        var target = Server(request.ServerAlias);
        target.Identity.Group = Copy(request.Group!);
        target.Identity.ServerProjectName = request.ServerProjectName;
        var result = new MultiuserLocalSessionsInfo { RemoteIdentity = target.Identity };
        result.ConnectionObservation = Read(target, () =>
        {
            var project = Project(target.Server, request.Group!, request.ServerAlias, request.ServerProjectName);
            var rows = target.Server.GetLocalSessions(project) ?? throw Incomplete();
            var ids = new HashSet<int>();
            foreach (var row in rows)
            {
                if (row is null || row.ProjectFileInfo is null || !ids.Add(row.SessionId)) throw Incomplete();
                var path = row.ProjectFileInfo.FullName;
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw Incomplete();
                result.Sessions.Add(new MultiuserLocalSessionInfo { SessionId = row.SessionId, ProjectPath = Path.GetFullPath(path) });
            }
        });
        return result;
    }

    public MultiuserLockStateInfo GetLockState(MultiuserLockStateRequest request)
    {
        Validate(request.ServerAlias, request.Group);
        RequireName(request.ServerProjectName);
        Connect(request.PortalProcessId);
        var target = Server(request.ServerAlias);
        target.Identity.Group = Copy(request.Group!);
        target.Identity.ServerProjectName = request.ServerProjectName;
        var result = new MultiuserLockStateInfo { RemoteIdentity = target.Identity };
        result.ConnectionObservation = Read(target, () =>
        {
            var project = Project(target.Server, request.Group!, request.ServerAlias, request.ServerProjectName);
            var provider = target.Server.GetLockStateProvider(project) ?? throw Incomplete();
            result.IsLocked = provider.IsProjectLocked();
            if (result.IsLocked)
            {
                result.Owner = provider.GetLockOwner();
                if (string.IsNullOrWhiteSpace(result.Owner)) throw Incomplete();
                if (!provider.IsProjectLocked())
                    throw new WorkerOperationException(WorkerFailureCategories.StateChanged, "The project lock changed during observation. Inspect again before relying on the result.");
            }
            result.ObservedAt = DateTimeOffset.UtcNow;
        });
        return result;
    }

    private void Connect(int? pid)
    {
        _session.EnsurePortalConnected(pid);
    }

    private List<(ProjectServer Server, MultiuserRemoteIdentity Identity)> Connections()
    {
        try
        {
            var rows = new List<(ProjectServer Server, MultiuserRemoteIdentity Identity)>();
            foreach (var server in _session.TiaPortal!.ProjectServers)
            {
                if (server is null) throw Incomplete();
                var alias = server.ServerName;
                var host = server.Host;
                var port = server.Port;
                if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535) throw Incomplete();
                rows.Add((server, new MultiuserRemoteIdentity { ServerAlias = alias, Host = host, Port = port }));
            }
            _observations.RetainConfiguredEndpoints(rows.Select(row => row.Identity), _session.PortalAttachmentRevision);
            return rows;
        }
        catch (WorkerOperationException) { throw; }
        catch (Exception) { throw Incomplete(); }
    }

    private (ProjectServer Server, MultiuserRemoteIdentity Identity) Server(string alias)
        => Exact(Connections(), row => row.Identity.ServerAlias == alias);

    private static List<ProjectServerGroup> Groups(ProjectServer server)
    {
        var groups = server.GetProjectServerGroups()?.ToList() ?? throw Incomplete();
        if (groups.Any(group => group is null || string.IsNullOrWhiteSpace(group.Name))) throw Incomplete();
        return groups;
    }

    private static List<ServerProjectInfo> Projects(ProjectServer server, ProjectServerGroupIdentity group, string alias)
    {
        var rows = (group.IsRoot ? server.GetServerProjects() : Exact(Groups(server), candidate => candidate.Name == group.Name).GetServerProjects())
            ?.ToList() ?? throw Incomplete();
        foreach (var row in rows)
        {
            if (row is null || string.IsNullOrWhiteSpace(row.ProjectName) || string.IsNullOrWhiteSpace(row.ServerAlias)) throw Incomplete();
            if (!string.Equals(row.ServerAlias, alias, StringComparison.Ordinal))
                throw new WorkerOperationException(WorkerFailureCategories.TargetEvidenceMismatch, "A returned server project does not match the requested server alias.");
        }
        return rows;
    }

    private static ServerProjectInfo Project(ProjectServer server, ProjectServerGroupIdentity group, string alias, string name)
        => Exact(Projects(server, group, alias), candidate => candidate.ProjectName == name);

    private static T Exact<T>(IEnumerable<T> rows, Func<T, bool> matches)
    {
        var selected = rows.Where(matches).ToList();
        if (selected.Count == 0) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, "No remote target matches the exact selector.");
        if (selected.Count != 1) throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, "Multiple remote targets match the exact selector.");
        return selected[0];
    }

    private ProjectServerConnectionObservation Read((ProjectServer Server, MultiuserRemoteIdentity Identity) target, Action body)
    {
        try { body(); return Observe(target.Identity, ProjectServerConnectionStates.Connected); }
        catch (WorkerOperationException) { throw; }
        catch (Exception)
        {
            Observe(target.Identity, ProjectServerConnectionStates.Unknown);
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "Project Server inventory could not be completed. Connectivity and authentication are not proven.");
        }
    }

    private ProjectServerConnectionObservation Observe(MultiuserRemoteIdentity identity, string state)
        => _observations.Observe(identity, _session.PortalAttachmentRevision, state,
            ProjectServerConnectionObservationSources.ExplicitRead);

    private static void Validate(string alias, ProjectServerGroupIdentity? group)
    {
        RequireName(alias);
        if (group is null || (group.IsRoot ? group.Name is not null : string.IsNullOrWhiteSpace(group.Name)))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "An explicit coherent root or named group is required.");
    }
    private static void RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "An exact nonblank name is required.");
    }
    private static ProjectServerGroupIdentity Copy(ProjectServerGroupIdentity group) => new() { IsRoot = group.IsRoot, Name = group.Name };
    private static WorkerOperationException Incomplete() => new(WorkerFailureCategories.WorkerOperationFailed, "Project Server returned incomplete or unreadable inventory evidence.");
}
