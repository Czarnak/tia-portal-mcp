using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Safety.Pipeline.GuardedWriteResponseBudget;

namespace TiaMcpServer.Network;

/// <summary>Bounds delivery copies only; plans, worker results and audit item truth remain intact.</summary>
public static class NetworkWritePayloadBudget
{
    private const string Guidance = "Inspect current state with filtered or paged network_read. Use the original exact selector; if unavailable, discover it first. Do not replay the write.";
    private const string DiagnosticSummary = "Diagnostic details omitted. Inspect current state with network_read.";
    private const int DiagnosticChars = 512;
    private static readonly GuardedWriteResponseBudget Budget = new("network_read", Guidance, DiagnosticSummary);
    public static NetworkGuardedWriteResponse Apply(NetworkGuardedWriteResponse response,
        int? maxItemChars = null, int? maxDocumentChars = null)
        => ApplyCore(response,
            maxItemChars ?? StructuredOperationBatchPayloadBudget.MaxItemChars,
            maxDocumentChars ?? StructuredOperationBatchPayloadBudget.MaxDocumentChars);

    private static NetworkGuardedWriteResponse ApplyCore(NetworkGuardedWriteResponse response, int maxItemChars, int maxDocumentChars)
    {
        var originalChars = Size(response);
        var current = Diagnostics(response, false, originalChars, maxDocumentChars);
        // Keep the ordinary diagnostic cap, then compact all diagnostics before dropping whole values.
        if (Size(current) > maxDocumentChars || AnyOverItemLimit(current, maxItemChars))
            current = Diagnostics(current, true, originalChars, maxDocumentChars);
        var effects = current.Effects.ToArray();
        var verification = current.Verification;

        NetworkGuardedWriteResponse Present(StructuredOperationBatch? batch)
        {
            var result = current with
            {
                Effects = effects.ToArray(),
                Batch = batch ?? current.Batch,
                Verification = verification,
            };
            return result with { Success = response.Success && !HasOmission(result.Omission,
                effects.Select(e => e.Omission), result.Batch, verification?.Omission) };
        }

        void OmitEffect(int i, string reason, int limit) => effects[i] = effects[i] with
        {
            Effect = null, Omission = Budget.Omission(reason, limit, Size(effects[i].Effect)),
        };
        void OmitChecks(string reason, int limit) => verification = verification! with
        {
            Operations = Array.Empty<WriteOperationVerification>(), Omission = Budget.Omission(reason, limit, Size(verification.Operations)),
        };

        IEnumerable<GuardedWriteBudgetRoot> ItemRoots()
        {
            for (var i = 0; i < effects.Length; i++)
            {
                var index = i;
                if (effects[i].Effect is { } remaining)
                    yield return new(Size(remaining), (reason, limit) => OmitEffect(index, reason, limit));
            }
            if (verification is { Operations.Count: > 0 })
                yield return new(Size(verification.Operations), OmitChecks);
        }

        IEnumerable<GuardedWriteBudgetRoot> DocumentRoots()
            => effects.Select((e, i) => (Kind: 1, Index: i, Chars: e.Effect is null ? 0 : Size(e.Effect)))
                .Append((Kind: 0, Index: 0, Chars: verification is { Operations.Count: > 0 } ? Size(verification.Operations) : 0))
                .Where(c => c.Chars > 0).OrderBy(c => c.Kind).ThenByDescending(c => c.Chars).ThenBy(c => c.Index)
                .Select(candidate => new GuardedWriteBudgetRoot(candidate.Chars, (reason, limit) =>
                {
                    if (candidate.Kind == 0) OmitChecks(reason, limit);
                    else if (effects[candidate.Index].Effect is not null) OmitEffect(candidate.Index, reason, limit);
                }));

        return Budget.Apply(Present,
            () => current.Batch,
            ItemRoots(), DocumentRoots, maxItemChars, maxDocumentChars);
    }

    /// <summary>Reserves exact encoded identities and bounded diagnostic/omission shapes before binding.</summary>
    internal static int MeasureProtectedCore(IReadOnlyList<NetworkOperationRequest> items)
        => Budget.MeasureProtectedCore(items.Select(i => (i.OperationId, i.Operation)).ToArray(), 2, 1, false,
            core => new NetworkGuardedWriteResponse("network_write", NetworkWriteContractVersion.Current, WritePhases.Applied, false,
                core.Error, core.Warnings, core.Guards,
                items.Select(i => new NetworkWriteEffectPresentation(i.OperationId, null, core.Omission)).ToArray(), core.Batch,
                new(false, items.Select(i => new WriteOperationVerification(i.OperationId, false, "trusted", null, null, DiagnosticSummary)).ToArray(),
                    core.Omission), core.Omission));

    public static int MeasurePreparedCore(IReadOnlyList<NetworkOperationRequest> items, IReadOnlyList<ItemPlan<NetworkWriteEffect>> plans)
    {
        long chars = MeasureProtectedCore(items);
        foreach (var plan in plans)
        {
            var effectChars = Size(plan.Effect);
            if (effectChars > StructuredOperationBatchPayloadBudget.MaxItemChars) return int.MaxValue;
            chars += effectChars;
        }
        return chars > int.MaxValue ? int.MaxValue : (int)chars;
    }

    private static bool AnyOverItemLimit(NetworkGuardedWriteResponse response, int maxItemChars)
        => response.Effects.Any(e => e.Effect is not null && Size(e.Effect) > maxItemChars)
            || response.Batch?.Operations.Any(i => i.Result is not null && Size(i.Result) > maxItemChars) == true
            || response.Verification is { Operations.Count: > 0 } v && Size(v.Operations) > maxItemChars;

    private static NetworkGuardedWriteResponse Diagnostics(NetworkGuardedWriteResponse response, bool compact, int originalChars, int maxDocumentChars)
    {
        var diagnostics = Budget.Diagnostics(omitWarnings: true, messageLimit: compact ? null : DiagnosticChars);
        var batch = diagnostics.Batch(response.Batch, item => item.Result is { } result
            ? NetworkPayloadContract.CompactWriteDiagnostics(result, diagnostics.Message, diagnostics.Warnings) : item.Result);
        var verification = response.Verification is null ? null : response.Verification with
        { Operations = response.Verification.Operations.Select(o => o with { Message = o.Message is null ? null : diagnostics.Message(o.Message) }).ToArray() };
        return response with { Error = diagnostics.Error(response.Error), Warnings = diagnostics.Warnings(response.Warnings),
            Guards = diagnostics.Guards(response.Guards), Batch = batch, Verification = verification,
            Omission = Budget.DiagnosticOmission(diagnostics, response.Omission, compact ? maxDocumentChars : DiagnosticChars, originalChars) };
    }
}
