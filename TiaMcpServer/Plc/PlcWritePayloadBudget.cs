using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Safety.Pipeline.GuardedWriteResponseBudget;

namespace TiaMcpServer.Plc;

/// <summary>
/// Bounds the delivered plc_write document only; plans, worker results and audit item truth stay
/// intact. When the document or an item is over budget, diagnostics are compacted first; then whole values are omitted, never cut: content
/// diffs, failure-side block import outcomes, verification checks and effects, and finally batch
/// results through the shared batch budget.
/// </summary>
public static class PlcWritePayloadBudget
{
    private const string RetryTool = "plc_read";
    private const string Guidance = "Inspect current state with plc_read (list_tag_tables, get_block_content or get_type_content); use smaller plc_write calls to see every effect. Do not replay the write.";
    private const string DiagnosticSummary = "Diagnostic details omitted. Inspect current state with plc_read.";
    private static readonly GuardedWriteResponseBudget Budget = new(RetryTool, Guidance, DiagnosticSummary);

    public static PlcGuardedWriteResponse Apply(PlcGuardedWriteResponse response,
        int? maxItemChars = null, int? maxDocumentChars = null)
        => ApplyCore(response,
            maxItemChars ?? StructuredOperationBatchPayloadBudget.MaxItemChars,
            maxDocumentChars ?? StructuredOperationBatchPayloadBudget.MaxDocumentChars);

    private static PlcGuardedWriteResponse ApplyCore(PlcGuardedWriteResponse response, int maxItemChars, int maxDocumentChars)
    {
        var originalChars = Size(response);
        var current = response;
        // Diagnostics are delivered intact while the document fits; they yield before any whole value is dropped.
        if (originalChars > maxDocumentChars || AnyOverItemLimit(current, maxItemChars))
            current = Compact(current, originalChars, maxDocumentChars);
        var effects = current.Effects.ToArray();
        var items = current.Batch?.Operations.ToArray();
        var verification = current.Verification;

        PlcGuardedWriteResponse Present(StructuredOperationBatch? batch)
        {
            var result = current with
            {
                Effects = effects.ToArray(),
                Batch = batch ?? (items is null ? null : StructuredOperationBatch.FromItems(items.ToArray(), current.Batch!.Truncation)),
                Verification = verification,
            };
            return result with { Success = response.Success && !HasOmission(result.Omission,
                effects.Select(e => e.Omission), result.Batch, verification?.Omission) };
        }

        // A presentation whose diff was dropped keeps its effect and records the omission beside it.
        void OmitDiff(int i, string reason, int limit) => effects[i] = effects[i] with
        {
            Effect = effects[i].Effect! with { ContentDiff = null },
            Omission = Budget.Omission(reason, limit, Size(effects[i].Effect!.ContentDiff)),
        };
        void OmitEffect(int i, string reason, int limit) => effects[i] = effects[i] with
        {
            Effect = null, Omission = Budget.Omission(reason, limit, Size(effects[i].Effect)),
        };
        void OmitOutcome(int i, string reason, int limit) => items![i] = items[i] with
        {
            Failure = items[i].Failure! with { BlockImportOutcome = null },
            Omission = Budget.Omission(reason, limit, Size(items[i].Failure!.BlockImportOutcome)),
        };
        void OmitChecks(string reason, int limit) => verification = verification! with
        {
            Operations = Array.Empty<PlcOperationVerification>(), Omission = Budget.Omission(reason, limit, Size(verification.Operations)),
        };

        IEnumerable<GuardedWriteBudgetRoot> ItemRoots()
        {
            for (var i = 0; i < effects.Length; i++)
            {
                var index = i;
                if (effects[i].Effect is { ContentDiff: not null } effect)
                    yield return new(Size(effect), (reason, limit) => OmitDiff(index, reason, limit));
                if (effects[i].Effect is { } remaining)
                    yield return new(Size(remaining), (reason, limit) => OmitEffect(index, reason, limit));
            }
            for (var i = 0; i < (items?.Length ?? 0); i++)
            {
                var index = i;
                if (items![i].Failure?.BlockImportOutcome is { } outcome)
                    yield return new(Size(outcome), (reason, limit) => OmitOutcome(index, reason, limit));
            }
            if (verification is { Operations.Count: > 0 })
                yield return new(Size(verification.Operations), OmitChecks);
        }

        IEnumerable<GuardedWriteBudgetRoot> DocumentRoots()
            => effects.Select((e, i) => (Kind: 0, Index: i, Chars: e.Effect?.ContentDiff is null ? 0 : Size(e.Effect.ContentDiff)))
                .Concat((items ?? []).Select((o, i) => (Kind: 1, Index: i, Chars: o.Failure?.BlockImportOutcome is null ? 0 : Size(o.Failure.BlockImportOutcome))))
                .Append((Kind: 2, Index: 0, Chars: verification is { Operations.Count: > 0 } ? Size(verification.Operations) : 0))
                .Concat(effects.Select((e, i) => (Kind: 3, Index: i, Chars: e.Effect is null ? 0 : Size(e.Effect))))
                .Where(c => c.Chars > 0).OrderBy(c => c.Kind).ThenByDescending(c => c.Chars).ThenBy(c => c.Index)
                .Select(candidate => new GuardedWriteBudgetRoot(candidate.Chars, (reason, limit) =>
                {
                    switch (candidate.Kind)
                    {
                        case 0 when effects[candidate.Index].Effect?.ContentDiff is not null: OmitDiff(candidate.Index, reason, limit); break;
                        case 1: OmitOutcome(candidate.Index, reason, limit); break;
                        case 2: OmitChecks(reason, limit); break;
                        case 3 when effects[candidate.Index].Effect is not null: OmitEffect(candidate.Index, reason, limit); break;
                    }
                }));

        return Budget.Apply(Present,
            () => items is null ? null : StructuredOperationBatch.FromItems(items.ToArray(), current.Batch!.Truncation),
            ItemRoots(), DocumentRoots, maxItemChars, maxDocumentChars);
    }

    /// <summary>
    /// The worst compact shape of the call's protected roots: every identity copy, two guards per
    /// item plus the partial-write guard, every omission record and the batch truncation. Admission
    /// rejects a call whose protected core alone exceeds the document budget.
    /// </summary>
    internal static int MeasureProtectedCore(IReadOnlyList<PlcOperationRequest> items)
        => Budget.MeasureProtectedCore(items.Select(i => (i.OperationId, i.Operation)).ToArray(), 2, 1, false,
            core => new PlcGuardedWriteResponse("plc_write", PlcContractVersion.Current, WritePhases.Applied, false,
                core.Error, core.Warnings, core.Guards,
                items.Select(i => new PlcWriteEffectPresentation(i.OperationId, null, core.Omission)).ToArray(), core.Batch,
                new(false, items.Select(i => new PlcOperationVerification(i.OperationId, false, "contentHash", null, null, null, DiagnosticSummary)).ToArray(),
                    core.Omission), core.Omission));

    private static bool AnyOverItemLimit(PlcGuardedWriteResponse response, int maxItemChars)
        => response.Effects.Any(e => e.Effect is not null && Size(e.Effect) > maxItemChars)
            || response.Batch?.Operations.Any(i => i.Result is not null && Size(i.Result) > maxItemChars
                || i.Failure?.BlockImportOutcome is not null && Size(i.Failure.BlockImportOutcome) > maxItemChars) == true
            || response.Verification is { Operations.Count: > 0 } v && Size(v.Operations) > maxItemChars;

    /// <summary>Replaces every diagnostic message with a fixed summary.</summary>
    private static PlcGuardedWriteResponse Compact(PlcGuardedWriteResponse response, int originalChars, int maxDocumentChars)
    {
        var diagnostics = Budget.Diagnostics();
        var batch = diagnostics.Batch(response.Batch);
        var verification = response.Verification is null ? null : response.Verification with
        {
            Operations = response.Verification.Operations.Select(o => o with { Message = o.Message is null ? null : diagnostics.Message(o.Message) }).ToArray(),
        };
        var compacted = response with
        {
            Error = diagnostics.Error(response.Error),
            Warnings = diagnostics.Warnings(response.Warnings),
            Guards = diagnostics.Guards(response.Guards),
            Batch = batch,
            Verification = verification,
        };
        return compacted with { Omission = Budget.DiagnosticOmission(diagnostics, response.Omission, maxDocumentChars, originalChars) };
    }

}
