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
    public static Task<StructuredOperationBatch> ExecuteReadsAsync<T>(
        IReadOnlyList<T> operations,
        Func<T, Task<StructuredOperationItem>> execute)
        where T : IOperationBatchItem
        => ExecuteDirectReadsAsync(operations, execute);

    private static async Task<StructuredOperationBatch> ExecuteDirectReadsAsync<T>(
        IReadOnlyList<T> operations,
        Func<T, Task<StructuredOperationItem>> execute)
        where T : IOperationBatchItem
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(execute);
        var items = new List<StructuredOperationItem>(operations.Count);
        foreach (var operation in operations)
        {
            items.Add(await execute(operation).ConfigureAwait(false));
        }

        return StructuredOperationBatch.FromItems(items);
    }

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

    /// <summary>
    /// Executes writes sequentially and stops at the first operation that did not succeed. There
    /// is no rollback: an earlier operation that already ran stays applied.
    ///
    /// <para>
    /// The stop decision is made on the PROJECTED item, never on the raw worker outcome. A worker
    /// that reports success but returns a payload failing its declared contract has a mutation of
    /// unknown effect behind it, so that <c>protocol_error</c> must halt the batch exactly like an
    /// outright worker failure — checking <c>WorkerCallResult.Success</c> alone would march on
    /// into later writes on top of state nobody can describe.
    /// </para>
    /// </summary>
    public static async Task<StructuredOperationBatch> ApplyWritesAsync<T>(
        IReadOnlyList<T> operations,
        Func<T, Task<WorkerCallResult>> invoke,
        Func<T, WorkerCallResult, StructuredOperationItem> project)
        where T : IOperationBatchItem
    {
        var items = new List<StructuredOperationItem>(operations.Count);
        var stopped = false;
        foreach (var operation in operations)
        {
            if (stopped)
            {
                items.Add(new StructuredOperationItem(
                    operation.OperationId,
                    operation.Operation,
                    OperationBatchStatus.Skipped,
                    Result: null,
                    Failure: null,
                    Omission: null,
                    StructuredOperationSkipReasons.EarlierOperationFailed,
                    Array.Empty<string>()));
                continue;
            }

            var item = project(operation, await invoke(operation).ConfigureAwait(false));
            stopped = !string.Equals(item.Status, OperationBatchStatus.Succeeded, StringComparison.Ordinal);
            items.Add(item);
        }

        return StructuredOperationBatch.FromItems(items);
    }
}
