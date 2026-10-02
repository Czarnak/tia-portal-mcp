using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;

namespace TiaMcpServer.ProjectTree;

internal sealed record ProjectTreeRenderedResponse(
    BrowseProjectTreeResponse Response,
    string CanonicalText,
    bool IsSuccess,
    bool HasNextPage);

internal sealed class ProjectTreePageProjector
{
    private const int MaximumSerializationAttempts = 200;

    private readonly ProjectTreeCursorCodec _cursorCodec;
    private readonly int _maxResponseChars;
    private readonly Action<int>? _observeSerialization;

    internal ProjectTreePageProjector(
        ProjectTreeCursorCodec cursorCodec,
        int maxResponseChars = ProjectTreeContract.MaximumResponseChars,
        Action<int>? observeSerialization = null)
    {
        _cursorCodec = cursorCodec ?? throw new ArgumentNullException(nameof(cursorCodec));
        _maxResponseChars = maxResponseChars > 0
            ? maxResponseChars
            : throw new ArgumentOutOfRangeException(nameof(maxResponseChars));
        _observeSerialization = observeSerialization;
    }

    internal ProjectTreeRenderedResponse Project(
        ProjectTreeSnapshotView snapshot,
        int offset,
        int requestedPageSize)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (requestedPageSize is < ProjectTreeContract.MinimumPageSize or > ProjectTreeContract.MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedPageSize));
        }

        var budget = new SerializationBudget(_observeSerialization);
        var totalNodes = snapshot.Content.Nodes.Count;
        if (offset < 0 || (totalNodes == 0 ? offset != 0 : offset >= totalNodes))
        {
            return Failure(
                WorkerFailureCategories.CursorOutOfRange,
                "The requested project-tree cursor offset is outside the snapshot range.",
                budget);
        }

        if (totalNodes == 0)
        {
            var empty = TryPage(snapshot, offset, requestedPageSize, take: 0, budget);
            return empty.IsSuccess
                ? empty
                : Failure(
                    WorkerFailureCategories.ResultMetadataTooLarge,
                    "Project-tree response metadata exceeds the configured response limit.",
                    budget);
        }

        var maximumTake = Math.Min(requestedPageSize, totalNodes - offset);
        var largestRequested = TryPage(snapshot, offset, requestedPageSize, maximumTake, budget);
        if (largestRequested.IsSuccess)
        {
            return largestRequested;
        }

        var metadataOnly = TryPage(snapshot, offset, requestedPageSize, take: 0, budget);
        if (!metadataOnly.IsSuccess)
        {
            return Failure(
                WorkerFailureCategories.ResultMetadataTooLarge,
                "Project-tree response metadata exceeds the configured response limit.",
                budget);
        }

        ProjectTreeRenderedResponse? largestFittingPrefix = null;
        var low = 1;
        var high = maximumTake - 1;
        while (low <= high)
        {
            var take = low + ((high - low) / 2);
            var candidate = TryPage(snapshot, offset, requestedPageSize, take, budget);
            if (candidate.IsSuccess)
            {
                largestFittingPrefix = candidate;
                low = take + 1;
            }
            else
            {
                high = take - 1;
            }
        }

        return largestFittingPrefix ?? Failure(
            WorkerFailureCategories.ResultItemTooLarge,
            "A project-tree response item exceeds the configured response limit.",
            budget);
    }

    private ProjectTreeRenderedResponse TryPage(
        ProjectTreeSnapshotView snapshot,
        int offset,
        int requestedPageSize,
        int take,
        SerializationBudget budget)
    {
        var end = checked(offset + take);
        var nextCursor = end < snapshot.Content.Nodes.Count
            ? _cursorCodec.Encode(new ProjectTreeCursorState(snapshot.SnapshotId, snapshot.QueryHash, end,
                ProjectBindingCursorState.FromSnapshot(snapshot.HostBinding, preserveUnboundEpoch: true)))
            : null;
        var response = Success(
            snapshot,
            new ProjectTreePagination(offset, requestedPageSize, take, nextCursor),
            snapshot.Content.Nodes.Skip(offset).Take(take).ToArray());
        var text = budget.Serialize(response);
        return new ProjectTreeRenderedResponse(response, text, text.Length <= _maxResponseChars, nextCursor is not null);
    }

    private ProjectTreeRenderedResponse Failure(string category, string message, SerializationBudget budget)
    {
        var response = new BrowseProjectTreeResponse(
            ProjectTreeContract.Version,
            ProjectTreeStatuses.Failed,
            Result: null,
            new BrowseProjectTreeFailure(category, message),
            Warnings: Array.Empty<string>());
        var text = budget.Serialize(response);
        return new ProjectTreeRenderedResponse(response, text, IsSuccess: false, HasNextPage: false);
    }

    private BrowseProjectTreeResponse Success(
        ProjectTreeSnapshotView snapshot,
        ProjectTreePagination pagination,
        IReadOnlyList<ProjectTreeFlatNode> nodes)
        => new(
            ProjectTreeContract.Version,
            ProjectTreeStatuses.Succeeded,
            new BrowseProjectTreeResult(
                new ProjectTreeSnapshotMetadata(
                    snapshot.SnapshotId,
                    snapshot.CreatedAt,
                    snapshot.IdleExpiresAt,
                    snapshot.Content.Nodes.Count),
                snapshot.Content.Query,
                pagination,
                nodes),
            Failure: null,
            snapshot.Warnings);

    private sealed class SerializationBudget
    {
        private readonly Action<int>? _observeSerialization;
        private int _attempts;

        internal SerializationBudget(Action<int>? observeSerialization)
        {
            _observeSerialization = observeSerialization;
        }

        internal string Serialize<T>(T value)
        {
            if (_attempts >= MaximumSerializationAttempts)
            {
                throw new InvalidOperationException("Project-tree page projection exceeded its serialization-attempt limit.");
            }

            var text = CanonicalJson.Serialize(value);
            _attempts++;
            _observeSerialization?.Invoke(text.Length);
            return text;
        }
    }
}
