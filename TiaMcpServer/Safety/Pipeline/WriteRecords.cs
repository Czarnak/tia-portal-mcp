using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Safety.Pipeline;

/// <summary>A call-level failure: a closed failure category and a caller-facing message.</summary>
public sealed record WriteToolError(string Category, string Message);

/// <summary>What one item did, or would do, paired with the item's <c>operationId</c>.</summary>
public sealed record WriteEffect<TEffect>(string OperationId, TEffect Effect);

/// <summary>
/// One guarded write call as the tool received it: the project, the ordered items, whether this is
/// a dry run.
/// </summary>
public sealed record WriteCall<TItem>(
    string? ProjectPath,
    IReadOnlyList<TItem> Items,
    bool DryRun)
    where TItem : IOperationBatchItem;

/// <summary>The domain's input validation verdict, produced before any worker call.</summary>
public sealed record WriteValidation(bool IsValid, WriteToolError? Error)
{
    public static WriteValidation Valid() => new(true, Error: null);

    public static WriteValidation Invalid(string category, string message)
        => new(false, new WriteToolError(category, message));
}

/// <summary>
/// The resolved plan for one item: its effect, or the <c>operationId</c> of the earlier item it
/// depends on (then <see cref="Effect"/> is unknown until the item is re-planned), plus the
/// preconditions checked while resolving it.
/// </summary>
public sealed record ItemPlan<TEffect>(
    TEffect? Effect,
    string? DependsOn,
    IReadOnlyList<CheckedPrecondition> Preconditions)
{
    public static ItemPlan<TEffect> Resolved(
        TEffect effect,
        IReadOnlyList<CheckedPrecondition>? preconditions = null)
        => new(effect, DependsOn: null, preconditions ?? Array.Empty<CheckedPrecondition>());

    public static ItemPlan<TEffect> DependsOnItem(
        string operationId,
        IReadOnlyList<CheckedPrecondition>? preconditions = null)
        => new(default, operationId, preconditions ?? Array.Empty<CheckedPrecondition>());
}

/// <summary>The first pass: one <see cref="ItemPlan{TEffect}"/> per item, or a call-level failure.</summary>
public sealed record WritePlan<TEffect>(
    bool Success,
    IReadOnlyList<ItemPlan<TEffect>> Items,
    WriteToolError? Error)
{
    public static WritePlan<TEffect> Ok(IReadOnlyList<ItemPlan<TEffect>> items)
        => new(true, items, Error: null);

    public static WritePlan<TEffect> Fail(string category, string message)
        => new(false, Array.Empty<ItemPlan<TEffect>>(), new WriteToolError(category, message));
}

/// <summary>A dependent item's late re-plan, run just before its own mutation.</summary>
public sealed record ItemReplan<TEffect>(bool Success, ItemPlan<TEffect>? Plan, WriteToolError? Error)
{
    public static ItemReplan<TEffect> Ok(ItemPlan<TEffect> plan) => new(true, plan, Error: null);

    public static ItemReplan<TEffect> Fail(string category, string message)
        => new(false, Plan: null, new WriteToolError(category, message));
}

/// <summary>Everything the pipeline learned about one call, handed to the domain to compose its response.</summary>
public sealed record WriteReport<TEffect, TVerification>(
    string Phase,
    bool Success,
    WriteToolError? Error,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<WriteGuardReport> Guards,
    IReadOnlyList<WriteEffect<TEffect>> Effects,
    StructuredOperationBatch? Batch,
    TVerification? Verification);
