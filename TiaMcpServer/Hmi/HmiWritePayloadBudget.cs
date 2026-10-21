using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Safety.Pipeline.GuardedWriteResponseBudget;

namespace TiaMcpServer.Hmi;

/// <summary>Bounds presentation only; execution statuses and verification truth survive omitted values.</summary>
public static class HmiWritePayloadBudget
{
    private const string Guidance = "Inspect current state with hmi_read; use smaller hmi_write calls to see every effect. Do not replay the write.";
    private const string DiagnosticSummary = "Diagnostic details omitted. Inspect current state with hmi_read.";

    private static readonly GuardedWriteResponseBudget Budget = new("hmi_read", Guidance, DiagnosticSummary);

    public static HmiGuardedWriteResponse Apply(HmiGuardedWriteResponse response,
        int? maxItemChars = null, int? maxDocumentChars = null)
        => ApplyCore(response,
            maxItemChars ?? StructuredOperationBatchPayloadBudget.MaxItemChars,
            maxDocumentChars ?? StructuredOperationBatchPayloadBudget.MaxDocumentChars);

    private static HmiGuardedWriteResponse ApplyCore(HmiGuardedWriteResponse response, int maxItemChars, int maxDocumentChars)
    {
        var current = response;
        if (Size(response) > maxDocumentChars || Oversized(response, maxItemChars)) current = Compact(response, maxDocumentChars);
        var effects = current.Effects.ToArray();
        var verification = current.Verification;
        HmiGuardedWriteResponse Present(StructuredOperationBatch? batch)
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

    // An initial blocking plan never runs. Otherwise each item has at most two initial info guards
    // and two late info guards (messages may differ). The stopping item adds at most two blockers,
    // then the pipeline adds one partial-write guard. 4N+3 also bounds an initially blocked plan.
    internal static int MeasureProtectedCore(IReadOnlyList<HmiWriteOperationRequest> items)
        => Budget.MeasureProtectedCore(items.Select(i => (i.OperationId, i.Operation)).ToArray(), 4, 3, true,
            core => new HmiGuardedWriteResponse("hmi_write", HmiWriteContractVersion.Current, WritePhases.Applied, false,
                core.Error, core.Warnings, core.Guards,
                items.Select(i => new HmiWriteEffectPresentation(i.OperationId, null, core.Omission)).ToArray(), core.Batch,
                new(false, [], core.Omission), core.Omission));

    private static bool Oversized(HmiGuardedWriteResponse r, int limit)
        => OversizedBatchOrDiagnostics(r.Error, r.Warnings, r.Guards, r.Batch, limit)
            || r.Effects.Any(e => e.Effect is not null && Size(e.Effect) > limit)
            || r.Verification is { Operations.Count: > 0 } v && Size(v.Operations) > limit;

    private static HmiGuardedWriteResponse Compact(HmiGuardedWriteResponse response, int maxDocumentChars)
    {
        var diagnostics = Budget.Diagnostics();
        var batch = diagnostics.Batch(response.Batch, item =>
        {
            if (item.Result is not { } value) return item.Result;
            var outcome = CanonicalJson.DeserializeWorkerPayload<HmiWriteOutcomeInfo>(value.GetRawText());
            return outcome.CleanupMessage is null ? value
                : CanonicalJson.ToElement(outcome with { CleanupMessage = diagnostics.Message(outcome.CleanupMessage) });
        });
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
