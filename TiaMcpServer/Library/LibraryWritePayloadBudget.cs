using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Safety.Pipeline.GuardedWriteResponseBudget;

namespace TiaMcpServer.Library;

/// <summary>Bounds presentation only; execution statuses and verification truth survive omitted values.</summary>
public static class LibraryWritePayloadBudget
{
    private const string Guidance = "Inspect current state with library_read; use smaller library_write calls to see every effect. Do not replay the write.";
    private const string DiagnosticSummary = "Diagnostic details omitted. Inspect current state with library_read.";

    private static readonly GuardedWriteResponseBudget Budget = new("library_read", Guidance, DiagnosticSummary);

    public static LibraryGuardedWriteResponse Apply(LibraryGuardedWriteResponse response,
        int? maxItemChars = null, int? maxDocumentChars = null)
        => ApplyCore(response,
            maxItemChars ?? StructuredOperationBatchPayloadBudget.MaxItemChars,
            maxDocumentChars ?? StructuredOperationBatchPayloadBudget.MaxDocumentChars);

    private static LibraryGuardedWriteResponse ApplyCore(LibraryGuardedWriteResponse response, int maxItemChars, int maxDocumentChars)
    {
        var current = response;
        if (Size(response) > maxDocumentChars || Oversized(response, maxItemChars)) current = Compact(response, maxDocumentChars);
        var effects = current.Effects.ToArray();
        var verification = current.Verification;
        LibraryGuardedWriteResponse Present(StructuredOperationBatch? batch)
        {
            var result = current with { Effects = effects, Verification = verification, Batch = batch ?? current.Batch };
            return result with { Success = response.Success && !HasOmission(result.Omission,
                effects.Select(e => e.Omission), result.Batch, verification?.Omission) };
        }
        void OmitEffect(int index, string reason, int limit) => effects[index] = effects[index] with
        { Effect = null, Omission = Budget.Omission(reason, limit, Size(effects[index].Effect)) };
        void OmitChecks(string reason, int limit) => verification = verification! with
        { Operations = [], Omission = Budget.Omission(reason, limit, Size(verification.Operations)) };

        GuardedWriteBudgetRoot Effect(int i) => new(effects[i].Effect is null ? 0 : Size(effects[i].Effect),
            (reason, limit) => OmitEffect(i, reason, limit));
        GuardedWriteBudgetRoot Checks() => new(verification is { Operations.Count: > 0 } ? Size(verification.Operations) : 0, OmitChecks);
        IEnumerable<GuardedWriteBudgetRoot> ItemRoots()
            => effects.Select((_, i) => Effect(i)).Append(Checks());
        IEnumerable<GuardedWriteBudgetRoot> DocumentRoots()
            => new[] { Checks() }.Concat(effects.Select((_, i) => Effect(i)).OrderByDescending(e => e.Chars));

        return Budget.Apply(Present, () => current.Batch, ItemRoots(), DocumentRoots, maxItemChars, maxDocumentChars);
    }

    // Reserve all four domain guards plus the stopping blockers and partial-write guard.
    internal static int MeasureProtectedCore(IReadOnlyList<LibraryWriteItem> items)
        => Budget.MeasureProtectedCore(items.Select(i => (i.OperationId, i.Operation)).ToArray(), 4, 3, true,
            core => new LibraryGuardedWriteResponse("library_write", LibraryWriteContractVersion.Current, WritePhases.Applied, false,
                core.Error, core.Warnings, core.Guards,
                items.Select(i => new LibraryWriteEffectPresentation(i.OperationId, null, core.Omission)).ToArray(), core.Batch,
                new(false, [], core.Omission), core.Omission));

    private static bool Oversized(LibraryGuardedWriteResponse r, int limit)
        => OversizedBatchOrDiagnostics(r.Error, r.Warnings, r.Guards, r.Batch, limit)
            || r.Effects.Any(e => e.Effect is not null && Size(e.Effect) > limit)
            || r.Verification is { Operations.Count: > 0 } v && Size(v.Operations) > limit;

    private static LibraryGuardedWriteResponse Compact(LibraryGuardedWriteResponse response, int maxDocumentChars)
    {
        var diagnostics = Budget.Diagnostics();
        var batch = diagnostics.Batch(response.Batch);
        var verification = response.Verification is null ? null : response.Verification with
        { Operations = response.Verification.Operations.Select(o => o with { Message = o.Message is null ? null : diagnostics.Message(o.Message) }).ToArray() };
        var compacted = response with
        {
            Error = diagnostics.Error(response.Error),
            Warnings = diagnostics.Warnings(response.Warnings), Guards = diagnostics.Guards(response.Guards),
            Batch = batch, Verification = verification,
        };
        return compacted with { Omission = Budget.DiagnosticOmission(diagnostics, response.Omission, maxDocumentChars, Size(response)) };
    }
}
