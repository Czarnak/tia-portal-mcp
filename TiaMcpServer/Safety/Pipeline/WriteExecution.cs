using ModelContextProtocol.Protocol;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Safety.Pipeline;

/// <summary>
/// The guarded write pipeline: runs one write call through validate, verified binding, pinned
/// lease, plan, guards, dry-run stop, mutate, verify, audit, and report. A domain supplies the
/// resolver, guards, mutation, and response shape; the order and the safety rules live here.
/// </summary>
public sealed class WriteExecution
{
    /// <summary>
    /// The warning (and partial-write guard message) on the item that stopped a multi-item call
    /// after its own mutation was attempted.
    /// </summary>
    public const string PartialWriteMessage =
        "This call stopped at this operation. This operation and any earlier operation in the same call "
        + "may already have changed TIA state; no rollback was attempted. Re-read the affected objects "
        + "before retrying.";

    /// <summary>The partial-write warning for an item that stopped the call before its own mutation ran.</summary>
    public const string PartialWriteNotMutatedMessage =
        "This call stopped at this operation before it was changed. Earlier operations in the same call "
        + "stay applied; no rollback was attempted. Re-read the affected objects before retrying.";

    private readonly IWriteBindingGate _gate;
    private readonly IWriteAuditSink _audit;
    private readonly WriteGuardCatalog _catalog;
    private readonly TimeProvider _time;

    public WriteExecution(
        IWriteBindingGate gate,
        IWriteAuditSink audit,
        WriteGuardCatalog catalog,
        TimeProvider time)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Runs <paramref name="call"/> through the pipeline. <c>isError</c> is true only for the
    /// <c>error</c> and <c>blocked</c> phases; a write that ran and failed reports
    /// <c>success: false</c> without it.
    /// </summary>
    public Task<CallToolResult> RunAsync<TItem, TEffect, TVerification, TResponse>(
        IWriteDomain<TItem, TEffect, TVerification, TResponse> domain,
        WriteCall<TItem> call,
        WriteConfirmationContext? confirmation = null,
        IWriteBindingStrategy<TItem>? bindingStrategy = null,
        CancellationToken cancellationToken = default)
        where TItem : IOperationBatchItem
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(call);
        var run = new WriteRun<TItem, TEffect, TVerification, TResponse>(
            domain, call, _gate, _audit, _catalog, _time, bindingStrategy);
        return run.ExecuteAsync();
    }
}
