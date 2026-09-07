using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Worker;

namespace TiaMcpServer.ProjectTree;

/// <summary>
/// Owns the public project-tree browse lifecycle: one worker observation creates an immutable
/// snapshot, while cursor continuations project only from the process-local cache.
/// </summary>
public sealed class ProjectTreeBrowseCoordinator
{
    private const int MaximumSnapshotChars = 4_000_000;

    private readonly ProjectTreeCursorCodec _cursorCodec;
    private readonly ProjectTreeSnapshotStore _store;
    private readonly ProjectTreePageProjector _projector;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string?, IReadOnlyList<ProjectTreeSelectorSegment>?, int?, Task<WorkerCallResult>> _browseSnapshot;

    internal ProjectTreeBrowseCoordinator(
        OpennessWorkerClient workerClient,
        ProjectTreeCursorCodec cursorCodec,
        ProjectTreeSnapshotStore store,
        ProjectTreePageProjector projector,
        TimeProvider timeProvider)
        : this(
            cursorCodec,
            store,
            projector,
            timeProvider,
            (projectPath, startSelector, depth) => workerClient.BrowseProjectTreeV3SnapshotAsync(
                projectPath,
                startSelector,
                depth))
    {
        ArgumentNullException.ThrowIfNull(workerClient);
    }

    internal ProjectTreeBrowseCoordinator(
        ProjectTreeCursorCodec cursorCodec,
        ProjectTreeSnapshotStore store,
        ProjectTreePageProjector projector,
        TimeProvider timeProvider,
        Func<string?, IReadOnlyList<ProjectTreeSelectorSegment>?, int?, Task<WorkerCallResult>> browseSnapshot)
    {
        _cursorCodec = cursorCodec ?? throw new ArgumentNullException(nameof(cursorCodec));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _browseSnapshot = browseSnapshot ?? throw new ArgumentNullException(nameof(browseSnapshot));
    }

    internal async Task<ProjectTreeRenderedResponse> BrowseAsync(
        ProjectTreeBrowseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageSize = request.ResolvePageSize();
            return request.Cursor is null
                ? await BrowseInitialAsync(request, pageSize).ConfigureAwait(false)
                : BrowseContinuation(request, pageSize);
        }
        catch (ProjectTreeRequestException exception)
        {
            return Failure(exception.Category, exception.Message);
        }
        catch (ProjectTreeSelectionException exception)
        {
            return Failure(exception.Category, exception.Message);
        }
        catch (ProjectTreeCursorException exception)
        {
            return Failure(exception.Category, exception.Message);
        }
        catch (ProjectTreeProtocolException exception)
        {
            return Failure(exception.Category, exception.Message);
        }
    }

    private async Task<ProjectTreeRenderedResponse> BrowseInitialAsync(
        ProjectTreeBrowseRequest request,
        int pageSize)
    {
        ProjectTreeNodeTypes.Validate(request.StartSelector);
        var worker = await _browseSnapshot(
            request.ProjectPath,
            request.StartSelector,
            request.Depth).ConfigureAwait(false);
        if (!worker.Success)
        {
            return Failure(
                worker.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                worker.Error ?? "The project-tree worker operation failed.",
                worker.Warnings);
        }

        var observation = ProjectTreeWorkerPayloadContract.Decode(
            worker,
            request.StartSelector,
            request.Depth);
        var query = new ProjectTreeQuery(
            observation.ResolvedProjectPath,
            observation.CanonicalStartSelector,
            observation.Depth);
        var nodes = ProjectTreeFlattener.Flatten(observation.Roots);
        var content = new ProjectTreeSnapshotContent(query, nodes);
        var chars = CanonicalJson.Serialize(content).Length;
        if (chars > MaximumSnapshotChars)
        {
            return Failure(
                WorkerFailureCategories.SnapshotTooLarge,
                "The filtered project-tree snapshot exceeds the 4,000,000-character limit.");
        }

        var candidate = new ProjectTreeSnapshotCandidate(
            _store.CreateSnapshotId(),
            _timeProvider.GetUtcNow(),
            ProjectTreeBrowseRequest.CreateQueryHash(query),
            content,
            observation.Warnings.ToArray(),
            chars);
        return _store.ProjectInitial(
            candidate,
            view => _projector.Project(view, 0, pageSize),
            projection => projection.IsSuccess && projection.HasNextPage);
    }

    private ProjectTreeRenderedResponse BrowseContinuation(
        ProjectTreeBrowseRequest request,
        int pageSize)
    {
        var state = _cursorCodec.Decode(request.Cursor!);
        var access = _store.Access(
            state.SnapshotId,
            snapshot =>
            {
                if (!string.Equals(state.QueryHash, snapshot.QueryHash, StringComparison.Ordinal))
                {
                    return Failure(
                        WorkerFailureCategories.CursorFilterMismatch,
                        "The cursor query does not match the cached snapshot.");
                }

                var mismatch = request.ValidateRepeatedQuery(snapshot.Content.Query);
                if (mismatch is not null)
                {
                    return Failure(WorkerFailureCategories.CursorFilterMismatch, mismatch);
                }

                return _projector.Project(snapshot, state.Offset, pageSize);
            },
            projection => projection.IsSuccess);

        return access.Found
            ? access.Value!
            : Failure(
                WorkerFailureCategories.SnapshotUnavailable,
                "The project-tree snapshot is no longer available; start again without a cursor.");
    }

    private static ProjectTreeRenderedResponse Failure(
        string category,
        string message,
        IReadOnlyList<string>? warnings = null)
    {
        var response = new BrowseProjectTreeResponse(
            ProjectTreeContract.Version,
            ProjectTreeStatuses.Failed,
            Result: null,
            new BrowseProjectTreeFailure(category, message),
            warnings?.ToArray() ?? Array.Empty<string>());
        return new ProjectTreeRenderedResponse(
            response,
            CanonicalJson.Serialize(response),
            IsSuccess: false,
            HasNextPage: false);
    }
}
