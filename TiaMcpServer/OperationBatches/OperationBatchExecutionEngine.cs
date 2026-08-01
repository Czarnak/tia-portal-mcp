using TiaMcpServer.Worker;

namespace TiaMcpServer.OperationBatches;

public static class OperationBatchExecutionEngine
{
    public static async Task<IReadOnlyList<OperationBatchResult>> ExecuteReadsAsync<T>(
        IReadOnlyList<T> operations,
        Func<T, Task<WorkerCallResult>> invoke)
        where T : IOperationBatchItem
    {
        var results = new List<OperationBatchResult>(operations.Count);
        foreach (var operation in operations)
        {
            results.Add(ToResult(operation, await invoke(operation).ConfigureAwait(false)));
        }

        return results;
    }

    public static async Task<IReadOnlyList<OperationBatchResult>> ApplyWritesAsync<T>(
        IReadOnlyList<T> operations,
        Func<T, Task<WorkerCallResult>> invoke)
        where T : IOperationBatchItem
    {
        var results = new List<OperationBatchResult>(operations.Count);
        var stopped = false;
        foreach (var operation in operations)
        {
            if (stopped)
            {
                results.Add(new OperationBatchResult(
                    operation.OperationId,
                    operation.Operation,
                    OperationBatchStatus.Skipped,
                    null));
                continue;
            }

            var workerResult = await invoke(operation).ConfigureAwait(false);
            stopped = !workerResult.Success;
            results.Add(ToResult(operation, workerResult));
        }

        return results;
    }

    private static OperationBatchResult ToResult(
        IOperationBatchItem operation,
        WorkerCallResult workerResult)
        => new(
            operation.OperationId,
            operation.Operation,
            workerResult.Success ? OperationBatchStatus.Succeeded : OperationBatchStatus.Failed,
            workerResult.ToText(),
            workerResult.Warnings.Count == 0 ? null : workerResult.Warnings);
}
