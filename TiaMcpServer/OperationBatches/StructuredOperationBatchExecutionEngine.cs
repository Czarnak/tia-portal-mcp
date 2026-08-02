using TiaMcpServer.Worker;

namespace TiaMcpServer.OperationBatches;

/// <summary>
/// Runs a batch of operations and projects each worker outcome through a caller-supplied contract
/// into <see cref="StructuredOperationItem"/>s.
///
/// <para>
/// The projection is what decides an item's final status, so a payload that fails its declared
/// contract is a first-class failed item rather than a silently-succeeded one carrying unusable
/// data. The engine itself stays domain-free: it knows nothing about Network, PLC, or HMI.
/// </para>
/// </summary>
public static class StructuredOperationBatchExecutionEngine
{
    /// <summary>
    /// Executes reads in request order. Reads are independent: execution continues after a worker
    /// failure and after a payload projection failure, so one bad operation never hides the rest.
    /// </summary>
    public static async Task<StructuredOperationBatch> ExecuteReadsAsync<T>(
        IReadOnlyList<T> operations,
        Func<T, Task<WorkerCallResult>> invoke,
        Func<T, WorkerCallResult, StructuredOperationItem> project)
        where T : IOperationBatchItem
    {
        var items = new List<StructuredOperationItem>(operations.Count);
        foreach (var operation in operations)
        {
            var workerResult = await invoke(operation).ConfigureAwait(false);
            items.Add(project(operation, workerResult));
        }

        return StructuredOperationBatch.FromItems(items);
    }
}
