using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Hmi;

/// <summary>Dispatches a validated hmi_read operation to the worker client.</summary>
public static class HmiWorkerInvoker
{
    public static Task<WorkerCallResult> InvokeReadAsync(
        OpennessWorkerClient client,
        HmiPageCursorCodec codec,
        HmiOperationRequest operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HmiOperationCatalog.IsOperation(operation.Operation))
        {
            return Task.FromResult(WorkerCallResult.Fail(
                WorkerFailureCategories.ValidationError,
                $"Unsupported HMI read operation '{operation.Operation}'."));
        }

        var query = HmiOperationCatalog.ToQuery(operation);
        if (operation.Cursor is not null)
        {
            // Cursor failures are item failures returned before any worker call.
            var failure = ResolveCursor(client, codec, operation, out var state);
            if (failure is not null)
            {
                return Task.FromResult(failure);
            }

            query.Offset = state!.Offset;
            query.SnapshotId = state.SnapshotId;
        }

        return client.ReadHmiAsync(
            HmiOperationCatalog.WorkerMethod(operation.Operation),
            query,
            projectPath: null);
    }

    private static WorkerCallResult? ResolveCursor(
        OpennessWorkerClient client,
        HmiPageCursorCodec codec,
        HmiOperationRequest operation,
        out HmiPageCursorState? state)
    {
        state = null;
        try
        {
            state = codec.Decode(operation.Cursor!);
        }
        catch (HmiPageCursorException exception)
        {
            return WorkerCallResult.Fail(exception.Category,
                "The cursor is invalid or was issued by an earlier server process; restart without a cursor.");
        }

        if (!string.Equals(state.Operation, operation.Operation, StringComparison.Ordinal)
            || !string.Equals(state.QueryHash, HmiPageCursorCodec.QueryHash(operation), StringComparison.Ordinal))
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.CursorFilterMismatch,
                "The cursor was issued for a different operation or filters; repeat the original filters or restart without a cursor.");
        }

        if (!state.HostBinding.Matches(client.BindingSnapshot, preserveUnboundEpoch: true))
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.CursorBindingMismatch,
                "The project binding changed since the cursor was issued; restart without a cursor.");
        }

        return null;
    }
}
