using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker;

public static class NetworkObjectPageBuilder
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public static int ResolvePageSize(int? requestedPageSize)
    {
        var pageSize = requestedPageSize ?? DefaultPageSize;
        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedPageSize));
        }

        return pageSize;
    }

    public static NetworkObjectListInfo Build(
        IReadOnlyList<NetworkObjectSummaryInfo> orderedItems,
        int pageSize,
        int offset,
        string queryHash,
        string snapshotHash)
    {
        if (offset > orderedItems.Count)
        {
            throw new NetworkCursorException(WorkerFailureCategories.CursorOutOfRange);
        }

        var page = orderedItems.Skip(offset).Take(pageSize).ToList();
        var nextOffset = offset + page.Count;
        return new NetworkObjectListInfo
        {
            Items = page,
            TotalCount = orderedItems.Count,
            ReturnedCount = page.Count,
            NextCursor = nextOffset < orderedItems.Count
                ? NetworkObjectCursorCodec.Encode(nextOffset, queryHash, snapshotHash)
                : null,
        };
    }
}
