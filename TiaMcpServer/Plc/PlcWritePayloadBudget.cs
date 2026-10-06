using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// Bounds the delivered plc_write document only; plans, worker results and audit item truth stay
/// intact. Diagnostics are compacted first; then whole values are omitted, never cut: content
/// diffs, failure-side block import outcomes, verification checks and effects, and finally batch
/// results through the shared batch budget.
/// </summary>
public static class PlcWritePayloadBudget
{
    private const string RetryTool = "plc_read";
    private const string Guidance = "Inspect current state with plc_read (list_tag_tables, get_block_content or get_type_content); use smaller plc_write calls to see every effect. Do not replay the write.";
    private const string DiagnosticSummary = "Diagnostic details omitted. Inspect current state with plc_read.";
    private const string DiagnosticOmissionReason = "diagnosticDetailsOmitted";
    private const int DiagnosticChars = 512;

    public static PlcGuardedWriteResponse Apply(PlcGuardedWriteResponse response,
        int maxItemChars = StructuredOperationBatchPayloadBudget.MaxItemChars,
        int maxDocumentChars = StructuredOperationBatchPayloadBudget.MaxDocumentChars)
    {
        var originalChars = Size(response);
        var current = Compact(response, all: false, originalChars);
        // Every repeated diagnostic yields before any whole value is dropped.
        if (Size(current) > maxDocumentChars || AnyOverItemLimit(current, maxItemChars))
            current = Compact(current, all: true, originalChars);
        var effects = current.Effects.ToArray();
        var items = current.Batch?.Operations.ToArray();
        var verification = current.Verification;

        PlcGuardedWriteResponse Present(StructuredOperationBatch? batch = null)
        {
            var result = current with
            {
                Effects = effects.ToArray(),
                Batch = batch ?? (items is null ? null : StructuredOperationBatch.FromItems(items.ToArray(), current.Batch!.Truncation)),
                Verification = verification,
            };
            return result with { Success = response.Success && !HasOmission(result) };
        }

        bool Fits() => Size(Present()) <= maxDocumentChars;

        // A presentation whose diff was dropped keeps its effect and records the omission beside it.
        void OmitDiff(int i, string reason, int limit) => effects[i] = effects[i] with
        {
            Effect = effects[i].Effect! with { ContentDiff = null },
            Omission = Omission(reason, limit, Size(effects[i].Effect!.ContentDiff)),
        };
        void OmitEffect(int i, string reason, int limit) => effects[i] = effects[i] with
        {
            Effect = null, Omission = Omission(reason, limit, Size(effects[i].Effect)),
        };
        void OmitOutcome(int i, string reason, int limit) => items![i] = items[i] with
        {
            Failure = items[i].Failure! with { BlockImportOutcome = null },
            Omission = Omission(reason, limit, Size(items[i].Failure!.BlockImportOutcome)),
        };
        void OmitChecks(string reason, int limit) => verification = verification! with
        {
            Operations = Array.Empty<PlcOperationVerification>(), Omission = Omission(reason, limit, Size(verification.Operations)),
        };

        var itemReason = StructuredOperationBatchPayloadBudget.ItemLimitReason;
        for (var i = 0; i < effects.Length; i++)
        {
            if (effects[i].Effect is { ContentDiff: not null } effect && Size(effect) > maxItemChars) OmitDiff(i, itemReason, maxItemChars);
            if (effects[i].Effect is { } remaining && Size(remaining) > maxItemChars) OmitEffect(i, itemReason, maxItemChars);
        }

        for (var i = 0; i < (items?.Length ?? 0); i++)
            if (items![i].Failure?.BlockImportOutcome is { } outcome && Size(outcome) > maxItemChars) OmitOutcome(i, itemReason, maxItemChars);
        if (verification is { Operations.Count: > 0 } && Size(verification.Operations) > maxItemChars) OmitChecks(itemReason, maxItemChars);

        var documentReason = StructuredOperationBatchPayloadBudget.DocumentLimitReason;
        var candidates = effects.Select((e, i) => (Kind: 0, Index: i, Chars: e.Effect?.ContentDiff is null ? 0 : Size(e.Effect.ContentDiff)))
            .Concat((items ?? []).Select((o, i) => (Kind: 1, Index: i, Chars: o.Failure?.BlockImportOutcome is null ? 0 : Size(o.Failure.BlockImportOutcome))))
            .Append((Kind: 2, Index: 0, Chars: verification is { Operations.Count: > 0 } ? Size(verification.Operations) : 0))
            .Concat(effects.Select((e, i) => (Kind: 3, Index: i, Chars: e.Effect is null ? 0 : Size(e.Effect))))
            .Where(c => c.Chars > 0)
            .OrderBy(c => c.Kind).ThenByDescending(c => c.Chars).ThenBy(c => c.Index)
            .ToArray();
        foreach (var candidate in candidates)
        {
            if (Fits()) break;
            switch (candidate.Kind)
            {
                case 0 when effects[candidate.Index].Effect?.ContentDiff is not null: OmitDiff(candidate.Index, documentReason, maxDocumentChars); break;
                case 1: OmitOutcome(candidate.Index, documentReason, maxDocumentChars); break;
                case 2: OmitChecks(documentReason, maxDocumentChars); break;
                case 3 when effects[candidate.Index].Effect is not null: OmitEffect(candidate.Index, documentReason, maxDocumentChars); break;
            }
        }

        if (items is null) return Present();
        // The shared helper's omitted status describes delivery; keep the trusted execution status.
        var trusted = StructuredOperationBatch.FromItems(items.ToArray(), current.Batch!.Truncation);
        StructuredOperationBatch Restore(StructuredOperationBatch candidate) => StructuredOperationBatch.FromItems(
            candidate.Operations.Select((item, index) => item with { Status = trusted.Operations[index].Status }).ToArray(), candidate.Truncation);
        var bounded = StructuredOperationBatchPayloadBudget.Apply(trusted, candidate => Present(Restore(candidate)),
            RetryTool, _ => Guidance, maxItemChars, maxDocumentChars);
        return Present(Restore(bounded));
    }

    /// <summary>
    /// The worst compact shape of the call's protected roots: every identity copy, two guards per
    /// item plus the partial-write guard, every omission record and the batch truncation. Admission
    /// rejects a call whose protected core alone exceeds the document budget.
    /// </summary>
    internal static int MeasureProtectedCore(IReadOnlyList<PlcOperationRequest> items)
    {
        if (items.Count == 0) return 0;
        var omission = Omission(StructuredOperationBatchPayloadBudget.DocumentLimitReason, int.MaxValue, int.MaxValue);
        var category = new string('x', 64);
        var longestId = items.OrderByDescending(i => Size(i.OperationId)).First().OperationId;
        var guards = items.SelectMany(item => Enumerable.Repeat(new WriteGuardReport(category, "block", item.OperationId, DiagnosticSummary, null), 2))
            .Append(new(category, "block", longestId, DiagnosticSummary, null)).ToArray();
        var batch = new StructuredOperationBatch(items.Count, new(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue),
            items.Select(item => new StructuredOperationItem(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, null,
                new(category, DiagnosticSummary), omission, StructuredOperationSkipReasons.EarlierOperationFailed, Array.Empty<string>())).ToArray(),
            new(true, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, items.Select(i => i.OperationId).ToArray()));
        return Size(new PlcGuardedWriteResponse("plc_write", PlcContractVersion.Current, WritePhases.Applied, false,
            new(category, DiagnosticSummary), Array.Empty<string>(), guards,
            items.Select(item => new PlcWriteEffectPresentation(item.OperationId, null, omission)).ToArray(), batch,
            new(false, items.Select(item => new PlcOperationVerification(item.OperationId, false, "contentHash", null, null, null, DiagnosticSummary)).ToArray(), omission),
            omission));
    }

    private static bool AnyOverItemLimit(PlcGuardedWriteResponse response, int maxItemChars)
        => response.Effects.Any(e => e.Effect is not null && Size(e.Effect) > maxItemChars)
            || response.Batch?.Operations.Any(i => i.Result is not null && Size(i.Result) > maxItemChars
                || i.Failure?.BlockImportOutcome is not null && Size(i.Failure.BlockImportOutcome) > maxItemChars) == true
            || response.Verification is { Operations.Count: > 0 } v && Size(v.Operations) > maxItemChars;

    /// <summary>Replaces diagnostics longer than 512 characters (or all of them) with a fixed summary.</summary>
    private static PlcGuardedWriteResponse Compact(PlcGuardedWriteResponse response, bool all, int originalChars)
    {
        var omitted = false;
        string Message(string message)
        {
            if (message == DiagnosticSummary || !all && message.Length <= DiagnosticChars) return message;
            omitted = true;
            return DiagnosticSummary;
        }

        IReadOnlyList<string> Warnings(IReadOnlyList<string> warnings) => warnings.Select(Message).Distinct(StringComparer.Ordinal).ToArray();
        var batch = response.Batch is null ? null : StructuredOperationBatch.FromItems(response.Batch.Operations.Select(item => item with
        {
            Failure = item.Failure is null ? null : item.Failure with { Message = Message(item.Failure.Message) },
            Warnings = Warnings(item.Warnings),
        }).ToArray(), response.Batch.Truncation);
        var verification = response.Verification is null ? null : response.Verification with
        {
            Operations = response.Verification.Operations.Select(o => o with { Message = o.Message is null ? null : Message(o.Message) }).ToArray(),
        };
        var compacted = response with
        {
            Error = response.Error is null ? null : response.Error with { Message = Message(response.Error.Message) },
            Warnings = Warnings(response.Warnings),
            Guards = response.Guards.Select(g => g with { Message = Message(g.Message) }).ToArray(),
            Batch = batch,
            Verification = verification,
        };
        return omitted
            ? compacted with { Omission = new(DiagnosticOmissionReason, all ? StructuredOperationBatchPayloadBudget.MaxDocumentChars : DiagnosticChars, originalChars, RetryTool, Guidance) }
            : compacted;
    }

    private static bool HasOmission(PlcGuardedWriteResponse response) => response.Omission is not null
        || response.Effects.Any(e => e.Omission is not null)
        || response.Batch?.Operations.Any(i => i.Omission is not null) == true
        || response.Verification?.Omission is not null;

    private static StructuredOperationOmission Omission(string reason, int limit, int originalChars)
        => new(reason, limit, originalChars, RetryTool, Guidance);

    private static int Size<T>(T value) => CanonicalJson.Serialize(value).Length;
}
