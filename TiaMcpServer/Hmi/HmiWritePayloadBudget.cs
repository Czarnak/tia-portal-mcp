using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Hmi;

/// <summary>Bounds presentation only; execution statuses and verification truth survive omitted values.</summary>
public static class HmiWritePayloadBudget
{
    private const string Guidance = "Inspect current state with hmi_read; use smaller hmi_write calls to see every effect. Do not replay the write.";
    private const string DiagnosticSummary = "Diagnostic details omitted. Inspect current state with hmi_read.";

    public static HmiGuardedWriteResponse Apply(HmiGuardedWriteResponse response,
        int maxItemChars = StructuredOperationBatchPayloadBudget.MaxItemChars,
        int maxDocumentChars = StructuredOperationBatchPayloadBudget.MaxDocumentChars)
    {
        var current = response;
        if (Size(response) > maxDocumentChars || Oversized(response, maxItemChars)) current = Compact(response);
        var effects = current.Effects.ToArray();
        var verification = current.Verification;
        HmiGuardedWriteResponse Present(StructuredOperationBatch? batch = null)
        {
            var result = current with { Effects = effects, Verification = verification, Batch = batch ?? current.Batch };
            return result with { Success = response.Success && !HasOmission(result) };
        }
        void OmitEffect(int index, string reason, int limit) => effects[index] = effects[index] with
        { Effect = null, Omission = Omission(reason, limit, Size(effects[index].Effect)) };
        void OmitChecks(string reason, int limit) => verification = verification! with
        { Operations = [], Omission = Omission(reason, limit, Size(verification.Operations)) };

        for (var i = 0; i < effects.Length; i++)
            if (effects[i].Effect is { } effect && Size(effect) > maxItemChars)
                OmitEffect(i, StructuredOperationBatchPayloadBudget.ItemLimitReason, maxItemChars);
        if (verification is { Operations.Count: > 0 } && Size(verification.Operations) > maxItemChars)
            OmitChecks(StructuredOperationBatchPayloadBudget.ItemLimitReason, maxItemChars);
        if (Size(Present()) > maxDocumentChars && verification is { Operations.Count: > 0 })
            OmitChecks(StructuredOperationBatchPayloadBudget.DocumentLimitReason, maxDocumentChars);
        foreach (var candidate in effects.Select((e, i) => (Index: i, Chars: e.Effect is null ? 0 : Size(e.Effect))).OrderByDescending(e => e.Chars))
        {
            if (Size(Present()) <= maxDocumentChars) break;
            if (candidate.Chars > 0) OmitEffect(candidate.Index, StructuredOperationBatchPayloadBudget.DocumentLimitReason, maxDocumentChars);
        }
        if (current.Batch is null) return Present();
        var trusted = current.Batch;
        StructuredOperationBatch Restore(StructuredOperationBatch candidate) => StructuredOperationBatch.FromItems(
            candidate.Operations.Select((item, index) => item with { Status = trusted.Operations[index].Status }).ToArray(), candidate.Truncation);
        var bounded = StructuredOperationBatchPayloadBudget.Apply(trusted, candidate => Present(Restore(candidate)),
            "hmi_read", _ => Guidance, maxItemChars, maxDocumentChars);
        return Present(Restore(bounded));
    }

    // An initial blocking plan never runs. Otherwise each item has at most two initial info guards
    // and two late info guards (messages may differ). The stopping item adds at most two blockers,
    // then the pipeline adds one partial-write guard. 4N+3 also bounds an initially blocked plan.
    internal static int MeasureProtectedCore(IReadOnlyList<HmiWriteOperationRequest> items)
    {
        if (items.Count == 0) return 0;
        var omission = Omission(StructuredOperationBatchPayloadBudget.DocumentLimitReason, int.MaxValue, int.MaxValue);
        var category = new string('x', 64);
        var longest = items.OrderByDescending(i => Size(i.OperationId)).First().OperationId;
        var guards = items.SelectMany(i => Enumerable.Repeat(new WriteGuardReport(category, "block", i.OperationId, DiagnosticSummary, null), 4))
            .Concat(Enumerable.Repeat(new WriteGuardReport(category, "block", longest, DiagnosticSummary, null), 3)).ToArray();
        var batch = new StructuredOperationBatch(items.Count, new(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue),
            items.Select(i => new StructuredOperationItem(i.OperationId, i.Operation, OperationBatchStatus.Succeeded, null,
                new(category, DiagnosticSummary), omission, StructuredOperationSkipReasons.EarlierOperationFailed, [DiagnosticSummary])).ToArray(),
            new(true, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, items.Select(i => i.OperationId).ToArray()));
        return Size(new HmiGuardedWriteResponse("hmi_write", HmiWriteContractVersion.Current, WritePhases.Applied, false,
            new(category, DiagnosticSummary), [DiagnosticSummary], guards,
            items.Select(i => new HmiWriteEffectPresentation(i.OperationId, null, omission)).ToArray(), batch,
            new(false, [], omission), omission));
    }

    private static bool Oversized(HmiGuardedWriteResponse r, int limit)
        => (r.Error is not null && Size(r.Error.Message) > limit) || Size(r.Warnings) > limit
            || r.Guards.Any(g => Size(g.Message) > limit)
            || r.Effects.Any(e => e.Effect is not null && Size(e.Effect) > limit)
            || r.Batch?.Operations.Any(i => i.Result is not null && Size(i.Result) > limit
                || i.Failure is not null && Size(i.Failure.Message) > limit || Size(i.Warnings) > limit) == true
            || r.Verification is { Operations.Count: > 0 } v && Size(v.Operations) > limit;

    private static HmiGuardedWriteResponse Compact(HmiGuardedWriteResponse response)
    {
        var omitted = false;
        string Message(string message)
        {
            if (message == DiagnosticSummary) return message;
            omitted = true;
            return DiagnosticSummary;
        }
        IReadOnlyList<string> Warnings(IReadOnlyList<string> warnings) => warnings.Select(Message).Distinct(StringComparer.Ordinal).ToArray();
        var batch = response.Batch is null ? null : StructuredOperationBatch.FromItems(response.Batch.Operations.Select(item =>
        {
            var result = item.Result;
            if (result is { } value)
            {
                var outcome = CanonicalJson.DeserializeWorkerPayload<HmiWriteOutcomeInfo>(value.GetRawText());
                if (outcome.CleanupMessage is not null) result = CanonicalJson.ToElement(outcome with { CleanupMessage = Message(outcome.CleanupMessage) });
            }
            return item with { Result = result, Failure = item.Failure is null ? null : item.Failure with { Message = Message(item.Failure.Message) }, Warnings = Warnings(item.Warnings) };
        }).ToArray(), response.Batch.Truncation);
        var verification = response.Verification is null ? null : response.Verification with
        { Operations = response.Verification.Operations.Select(o => o with { Message = o.Message is null ? null : Message(o.Message) }).ToArray() };
        var compacted = response with
        {
            Error = response.Error is null ? null : response.Error with { Message = Message(response.Error.Message) },
            Warnings = Warnings(response.Warnings), Guards = response.Guards.Select(g => g with { Message = Message(g.Message) }).ToArray(),
            Batch = batch, Verification = verification,
        };
        return omitted ? compacted with { Omission = Omission("diagnosticDetailsOmitted", StructuredOperationBatchPayloadBudget.MaxDocumentChars, Size(response)) } : compacted;
    }
    private static bool HasOmission(HmiGuardedWriteResponse r) => r.Omission is not null || r.Effects.Any(e => e.Omission is not null)
        || r.Batch?.Operations.Any(i => i.Omission is not null) == true || r.Verification?.Omission is not null;
    private static StructuredOperationOmission Omission(string reason, int limit, int original) => new(reason, limit, original, "hmi_read", Guidance);
    private static int Size<T>(T value) => CanonicalJson.Serialize(value).Length;
}
