using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Safety.Pipeline;

/// <summary>
/// What a write domain supplies to the guarded pipeline: validation, a resolver (plan and re-plan),
/// a guard set, the mutation, the per-item projection, verification, and the response shape. The
/// pipeline owns the order, the binding gate and lease, guard decisions, and the audit.
/// </summary>
public interface IWriteDomain<TItem, TEffect, TVerification, TResponse>
    where TItem : IOperationBatchItem
{
    string ToolName { get; }

    string ContractVersion { get; }

    /// <summary>Validates the items and the access mode before any worker call.</summary>
    WriteValidation Validate(IReadOnlyList<TItem> items, McpAccessMode accessMode);

    /// <summary>Resolves every item against fresh state; one plan per item, in order.</summary>
    Task<WritePlan<TEffect>> PlanAsync(string? projectPath, IReadOnlyList<TItem> items);

    /// <summary>Pure: the guards the planned items fire. Plans align with items by index.</summary>
    IReadOnlyList<FiredGuard> EvaluateGuards(IReadOnlyList<TItem> items, IReadOnlyList<ItemPlan<TEffect>> plans);

    /// <summary>Re-resolves one dependent item just before its own mutation.</summary>
    Task<ItemReplan<TEffect>> ReplanAsync(string? projectPath, TItem item);

    Task<WorkerCallResult> MutateAsync(string? projectPath, TItem item);

    /// <summary>Projects one item's worker result into its typed per-item result.</summary>
    StructuredOperationItem Project(TItem item, WorkerCallResult result);

    /// <summary>The post-write read, or null when the domain performs none.</summary>
    Task<TVerification?> VerifyAsync(string? projectPath, StructuredOperationBatch batch);

    TResponse Compose(WriteReport<TEffect, TVerification> report);
}
