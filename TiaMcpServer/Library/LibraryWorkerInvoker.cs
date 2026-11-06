using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Cursors;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Library;

/// <summary>Dispatches a library_read item to the worker client; every rejection here is NotSent.</summary>
public static class LibraryWorkerInvoker
{
    public static Task<WorkerCallResult> InvokeReadAsync(
        OpennessWorkerClient client,
        LibraryPageCursorCodec codec,
        LibraryReadItem item,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (LibraryReadCatalog.Check(item) is { } invalid)
            return Task.FromResult(NotSent(WorkerFailureCategories.ValidationError, invalid));

        var query = LibraryReadCatalog.ToQuery(item);
        if (item.Cursor is not null)
        {
            var failure = ResolveCursor(client, codec, item, out var state);
            if (failure is not null) return Task.FromResult(failure);

            query.Offset = state!.Offset;
            query.SnapshotId = state.SnapshotId;
        }

        return client.ReadLibraryAsync(LibraryReadMethods.ForOperation(item.Operation, item.Scope), query);
    }

    private static WorkerCallResult? ResolveCursor(
        OpennessWorkerClient client,
        LibraryPageCursorCodec codec,
        LibraryReadItem item,
        out LibraryPageCursorState? state)
    {
        state = null;
        try
        {
            state = codec.Decode(item.Cursor!);
        }
        catch (CursorException exception)
        {
            return NotSent(exception.Category,
                "The cursor is invalid or was issued by an earlier server process; restart without a cursor.");
        }

        if (!string.Equals(state.Operation, item.Operation, StringComparison.Ordinal)
            || !string.Equals(state.QueryHash, LibraryPageCursorCodec.QueryHash(item), StringComparison.Ordinal))
        {
            return NotSent(WorkerFailureCategories.CursorFilterMismatch,
                "The cursor was issued for a different operation or selectors; repeat the original selectors or restart without a cursor.");
        }

        if (!state.HostBinding.Matches(client.BindingSnapshot))
        {
            return NotSent(WorkerFailureCategories.CursorBindingMismatch,
                "The project binding changed since the cursor was issued; restart without a cursor.");
        }

        return null;
    }

    private static WorkerCallResult NotSent(string category, string message)
        => WorkerCallResult.Fail(category, message) with { DispatchState = WorkerDispatchState.NotSent };
}
