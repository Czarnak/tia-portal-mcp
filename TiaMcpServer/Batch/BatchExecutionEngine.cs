using TiaMcpServer.Worker;

namespace TiaMcpServer.Batch;

/// <summary>
/// Orchestrates batch execution independently of the worker. The actual per-item call is
/// injected as a delegate so ordering, per-item read failures, and write stop-on-first-failure
/// can be unit-tested without a live TIA Openness worker.
/// </summary>
public static class BatchExecutionEngine
{
    /// <summary>Reads run independently; a failing item is recorded but never stops the others.</summary>
    public static async Task<IReadOnlyList<BatchOperationResult>> ExecuteReadsAsync(
        IReadOnlyList<BatchOperationRequest> operations,
        Func<BatchOperationRequest, Task<WorkerCallResult>> invoke)
    {
        var results = new List<BatchOperationResult>(operations.Count);
        foreach (var op in operations)
        {
            var result = await invoke(op).ConfigureAwait(false);
            results.Add(ToOperationResult(op, result));
        }

        return results;
    }

    /// <summary>Writes run sequentially and stop on the first failure; later items are skipped.</summary>
    public static async Task<IReadOnlyList<BatchOperationResult>> ApplyWritesAsync(
        IReadOnlyList<BatchOperationRequest> operations,
        Func<BatchOperationRequest, Task<WorkerCallResult>> invoke)
    {
        var results = new List<BatchOperationResult>(operations.Count);
        var stopped = false;
        foreach (var op in operations)
        {
            if (stopped)
            {
                results.Add(new BatchOperationResult(op.OperationId, op.Operation, BatchOperationStatus.Skipped, null));
                continue;
            }

            var result = await invoke(op).ConfigureAwait(false);
            stopped = !result.Success;
            results.Add(ToOperationResult(op, result));
        }

        return results;
    }

    private static BatchOperationResult ToOperationResult(BatchOperationRequest op, WorkerCallResult result)
        => new(
            op.OperationId,
            op.Operation,
            result.Success ? BatchOperationStatus.Succeeded : BatchOperationStatus.Failed,
            result.ToText(),
            result.Warnings.Count > 0 ? result.Warnings : null);
}
