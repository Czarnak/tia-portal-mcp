using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Multiuser;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Tools;

internal static class ProjectBindingInspectionPayloadContract
{
    internal static StandaloneToolOutcome<ProjectBindingInspectionInfo> Decode(WorkerRequest request, WorkerCallResult result)
    {
        if (!result.Success) return new(OperationBatchStatus.Failed, null, StandalonePayloadContract.Failure(result), null);
        try
        {
            if (result.PortalProcessId is null or <= 0 || request.PortalProcessId is { } pid && result.PortalProcessId != pid)
                throw new JsonException();
            var inspection = new ProjectBindingInspectionInfo(request.Method, result.PortalProcessId, null, null, null, null, null);
            inspection = request.Method switch
            {
                "list_server_connections" => inspection with { ServerConnections = Read<MultiuserServerConnectionsInfo>(value =>
                {
                    Rows(value.Connections);
                    foreach (var row in value.Connections) Endpoint(row.ServerAlias, row.Host, row.Port);
                }) },
                "list_server_groups" => inspection with { ServerGroups = Read<MultiuserServerGroupsInfo>(value =>
                {
                    Remote(value.RemoteIdentity, value.ConnectionObservation, request);
                    Rows(value.Groups);
                    foreach (var row in value.Groups) { Group(row); if (row.IsRoot) throw new JsonException(); }
                }) },
                "list_server_projects" => inspection with { ServerProjects = Read<MultiuserServerProjectsInfo>(value =>
                {
                    Remote(value.RemoteIdentity, value.ConnectionObservation, request);
                    Rows(value.Projects);
                    foreach (var row in value.Projects) Name(row.Name);
                }) },
                "list_local_sessions" => inspection with { LocalSessions = Read<MultiuserLocalSessionsInfo>(value =>
                {
                    Remote(value.RemoteIdentity, value.ConnectionObservation, request);
                    if (value.Scope != "currentMachineCurrentUser") throw new JsonException();
                    Rows(value.Sessions);
                    if (value.Sessions.Select(row => row.SessionId).Distinct().Count() != value.Sessions.Count) throw new JsonException();
                    foreach (var row in value.Sessions)
                        if (string.IsNullOrWhiteSpace(row.ProjectPath) || !Path.IsPathFullyQualified(row.ProjectPath)) throw new JsonException();
                }) },
                "get_lock_state" => inspection with { LockState = Read<MultiuserLockStateInfo>(value =>
                {
                    Remote(value.RemoteIdentity, value.ConnectionObservation, request);
                    if (value.ObservedAt == default || (value.IsLocked ? string.IsNullOrWhiteSpace(value.Owner) : value.Owner is not null)) throw new JsonException();
                }) },
                _ => throw new JsonException()
            };
            return new(OperationBatchStatus.Succeeded, inspection, null, null);

            T Read<T>(Action<T> validate) => CanonicalJson.NormalizeWorkerPayload(result.Payload, validate).Value;
        }
        catch (JsonException) { return StandalonePayloadContract.ProtocolFailure<ProjectBindingInspectionInfo>(); }
    }

    private static void Name(string? value) { if (string.IsNullOrWhiteSpace(value)) throw new JsonException(); }
    private static void Rows<T>(IReadOnlyList<T>? rows)
    { if (rows is null || rows.Any(row => row is null)) throw new JsonException(); }
    private static void Endpoint(string? alias, string? host, int? port)
    { Name(alias); Name(host); if (port is null or <= 0 or > 65535) throw new JsonException(); }
    private static void Group(ProjectServerGroupIdentity? group)
    { if (group is null || (group.IsRoot ? group.Name is not null : string.IsNullOrWhiteSpace(group.Name))) throw new JsonException(); }

    private static void Remote(MultiuserRemoteIdentity? remote, ProjectServerConnectionObservation? observation, WorkerRequest request)
    {
        if (remote is null || observation is null) throw new JsonException();
        Endpoint(remote.ServerAlias, remote.Host, remote.Port);
        if (remote.ServerAlias != request.MultiuserServerAlias || remote.Protocol is not null
            || remote.ServerProjectName != request.MultiuserServerProjectName || remote.LocalSessionId is not null || remote.LocalSessionPath is not null)
            throw new JsonException();
        if (request.MultiuserGroupIsRoot is { } root)
        {
            Group(remote.Group);
            if (remote.Group!.IsRoot != root || remote.Group.Name != request.MultiuserGroupName) throw new JsonException();
        }
        else if (remote.Group is not null) throw new JsonException();
        if (observation.State != ProjectServerConnectionStates.Connected
            || observation.ObservationSource != ProjectServerConnectionObservationSources.ExplicitRead
            || observation.ObservedAt == default
            || observation.PreviousState is not null && !ProjectServerConnectionStates.All.Contains(observation.PreviousState)
            || observation.Transition != (observation.PreviousState is not null && observation.PreviousState != observation.State))
            throw new JsonException();
    }
}
