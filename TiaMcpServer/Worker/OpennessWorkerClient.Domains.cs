using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Worker;

public partial class OpennessWorkerClient
{
    public Task<ProjectTreeSnapshotCallResult> BrowseProjectTreeV3SnapshotAsync(
        IReadOnlyList<ProjectTreeSelectorSegment>? startSelector = null,
        int? depth = null)
    {
        ProjectTreeNodeTypes.Validate(startSelector);
        return ExecuteSerializedBindingOperationAsync(async () =>
        {
            var result = await SendBoundProjectRequestCoreAsync(
                "browse_project_tree_v3_snapshot",
                null,
                request =>
                {
                    request.StartSelector = startSelector?.ToList();
                    request.Depth = depth;
                },
                "{}",
                BindingTransition.None).ConfigureAwait(false);
            return new ProjectTreeSnapshotCallResult(result, _projectSessionBinding.CaptureSnapshot());
        });
    }

    public Task<WorkerCallResult> ReadCrossReferencesAsync(
        CrossReferenceSelectorInfo selector,
        string? filter,
        int? maxResults)
    {
        // Validate the filter before TryResolveWithSnapshot so an invalid filter fails fast without a worker round-trip.
        if (!CrossReferenceFilterNames.TryNormalize(filter, out var normalizedFilter, out var filterError))
        {
            return Task.FromResult(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, filterError!));
        }

        return SendBoundProjectRequestAsync(
            "read_cross_references",
            null,
            request =>
            {
                request.CrossReferenceSelector = selector;
                request.CrossReferenceFilter = normalizedFilter;
                request.MaxResults = maxResults;
            },
            "{}");
    }

    /// <summary>Runs one standalone VCI read through the verified binding and capability gates.</summary>
    public Task<WorkerCallResult> ReadVciAsync(string method, TiaMcpServer.Contracts.Vci.VciQueryInfo query)
        => SendBoundProjectRequestAsync(method, null, request => request.VciQuery = query, "{}");

    public Task<WorkerCallResult> WriteVciAsync(string method, TiaMcpServer.Contracts.Vci.VciWriteRequestInfo write)
        => SendBoundProjectRequestAsync(method, null, request => request.VciWrite = write, "{}");

    /// <summary>Runs one hmi_read operation; <paramref name="method"/> is the hmi_-prefixed worker method.</summary>
    public Task<WorkerCallResult> ReadHmiAsync(string method, HmiQueryInfo query)
    {
        return SendBoundProjectRequestAsync(
            method,
            null,
            request => request.HmiQuery = query,
            "{}");
    }

    public Task<WorkerCallResult> WriteHmiAsync(string method, HmiWriteRequestInfo write)
        => SendBoundProjectRequestAsync(method, null, request => request.HmiWrite = write, "{}");
}
