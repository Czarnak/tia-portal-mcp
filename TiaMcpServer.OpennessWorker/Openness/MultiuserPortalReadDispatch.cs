using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>The Portal-only admission and identity seam; no project discovery or selection.</summary>
internal static class MultiuserPortalReadDispatch
{
    public static WorkerResponse Run(TiaPortalSession session, WorkerRequest request, Func<TiaPortalSession, WorkerResponse> body)
    {
        Validate(request);
        session.ValidateExpectedSessionIdentity(request.ExpectedSessionIdentity, true, useCachedIdentity: true);
        session.EnsurePortalConnected(request.PortalProcessId);
        session.ValidateExpectedSessionIdentity(request.ExpectedSessionIdentity, true);
        var response = body(session);
        // Read-side races must not certify a context that disappeared during remote inventory.
        session.EnsurePortalConnected(request.PortalProcessId);
        session.ValidateExpectedSessionIdentity(request.ExpectedSessionIdentity, true);
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
