using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Multiuser;

/// <summary>The Portal-only admission and identity seam; no project discovery or selection.</summary>
internal static class MultiuserPortalReadDispatch
{
    public static WorkerResponse Invoke(MultiuserInventoryService service, WorkerRequest request)
    {
        var group = request.MultiuserGroupIsRoot.HasValue
            ? new ProjectServerGroupIdentity { IsRoot = request.MultiuserGroupIsRoot.Value, Name = request.MultiuserGroupName }
            : null;
        return request.Method switch
        {
            "list_server_connections" => Success(service.ListServerConnections(new() { PortalProcessId = request.PortalProcessId })),
            "list_server_groups" => Success(service.ListServerGroups(new() { PortalProcessId = request.PortalProcessId, ServerAlias = request.MultiuserServerAlias! })),
            "list_server_projects" => Success(service.ListServerProjects(new() { PortalProcessId = request.PortalProcessId, ServerAlias = request.MultiuserServerAlias!, Group = group })),
            "list_local_sessions" => Success(service.ListLocalSessions(new() { PortalProcessId = request.PortalProcessId, ServerAlias = request.MultiuserServerAlias!, Group = group, ServerProjectName = request.MultiuserServerProjectName! })),
            "get_lock_state" => Success(service.GetLockState(new() { PortalProcessId = request.PortalProcessId, ServerAlias = request.MultiuserServerAlias!, Group = group, ServerProjectName = request.MultiuserServerProjectName! })),
            _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Unknown Portal inventory method.")
        };
    }

    private static WorkerResponse Success<T>(T payload) => new() { Success = true, Payload = WorkerJson.SerializePayload(payload) };

    public static WorkerResponse Run(TiaPortalSession session, WorkerRequest request, Func<TiaPortalSession, WorkerResponse> body)
    {
        Validate(request);
        session.ValidateExpectedSessionIdentity(request.ExpectedSessionIdentity, true, useCachedIdentity: true);
        session.EnsurePortalConnected(request.PortalProcessId);
        session.ValidateExpectedSessionIdentity(request.ExpectedSessionIdentity, true);
        var attachmentRevision = session.PortalAttachmentRevision;
        WorkerResponse response;
        try { response = body(session); }
        finally
        {
            // Validate failures too, but never auto-attach after loss during the read.
            if (!session.IsConnected || session.PortalAttachmentRevision != attachmentRevision)
                throw new WorkerOperationException(WorkerFailureCategories.BindingConflict, "The Portal attachment was lost during inventory. No inventory operation was replayed.");
            session.EnsurePortalConnected(request.PortalProcessId);
            session.ValidateExpectedSessionIdentity(request.ExpectedSessionIdentity, true);
        }
        response.PortalProcessId = session.CurrentProcessId;
        return response;
    }

    private static void Validate(WorkerRequest request)
    {
        var needsAlias = request.Method != "list_server_connections";
        var needsGroup = request.Method is "list_server_projects" or "list_local_sessions" or "get_lock_state";
        var needsProject = request.Method is "list_local_sessions" or "get_lock_state";
        if (request.Method is not ("list_server_connections" or "list_server_groups" or "list_server_projects" or "list_local_sessions" or "get_lock_state"))
            Reject();
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(WorkerRequest.Method), nameof(WorkerRequest.ProtocolVersion), nameof(WorkerRequest.ExpectedSessionIdentity), nameof(WorkerRequest.PortalProcessId)
        };
        if (needsAlias) allowed.Add(nameof(WorkerRequest.MultiuserServerAlias));
        if (needsGroup) { allowed.Add(nameof(WorkerRequest.MultiuserGroupIsRoot)); allowed.Add(nameof(WorkerRequest.MultiuserGroupName)); }
        if (needsProject) allowed.Add(nameof(WorkerRequest.MultiuserServerProjectName));
        var defaults = new WorkerRequest();
        foreach (var property in typeof(WorkerRequest).GetProperties())
            if (!allowed.Contains(property.Name) && !Equals(property.GetValue(request), property.GetValue(defaults))) Reject();
        if (request.PortalProcessId.HasValue && request.PortalProcessId <= 0) Reject();
        if (needsAlias && string.IsNullOrWhiteSpace(request.MultiuserServerAlias)) Reject();
        if (needsGroup && (!request.MultiuserGroupIsRoot.HasValue ||
            (request.MultiuserGroupIsRoot.Value ? request.MultiuserGroupName is not null : string.IsNullOrWhiteSpace(request.MultiuserGroupName)))) Reject();
        if (needsProject && string.IsNullOrWhiteSpace(request.MultiuserServerProjectName)) Reject();
    }

    private static void Reject() => throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
        "Invalid or inappropriate selectors for this Portal inventory operation.");
}
