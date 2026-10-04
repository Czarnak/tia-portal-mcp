using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Network;

/// <summary>Bounds delivery copies only; execution plans, worker results and audit item truth remain intact.</summary>
public static class NetworkWritePayloadBudget
{
    private const string Guidance = "Inspect current state with filtered or paged network_read. Use the original exact selector; if unavailable, discover it first. Do not replay the write.";
    private const string DiagnosticSummary = "Diagnostic details omitted. Inspect current state with network_read.";
    private const int DiagnosticChars = 512;

    public static NetworkGuardedWriteResponse Apply(NetworkGuardedWriteResponse response,
        int maxItemChars = 60000, int maxDocumentChars = 180000)
    {
        var originalChars = Size(response);
        var current = Diagnostics(response, false, originalChars);
        var effects = current.Effects.ToArray();
        var verification = current.Verification;
        var operations = verification?.Operations.ToArray();
        var finalChecks = verification?.FinalChecks;
        var finalOmission = verification?.Omission;

        NetworkGuardedWriteResponse Present(StructuredOperationBatch? batch = null)
        {
            var result = current with { Effects = effects.ToArray(), Batch = batch ?? current.Batch,
                Verification = verification is null ? null : verification with
                { Operations = operations!.ToArray(), FinalChecks = finalChecks!, Omission = finalOmission } };
            return result with { Success = response.Success && !HasOmission(result) };
        }

        void OmitEffect(int index, string reason, int limit)
        {
            effects[index] = effects[index] with { Effect = null,
                Omission = Omission(reason, limit, Size(effects[index].Effect)) };
        }
        void OmitEvidence(int index, string reason, int limit)
        {
            operations![index] = operations[index] with { Evidence = null,
                Omission = Omission(reason, limit, Size(operations[index].Evidence)) };
        }
        void OmitFinal(string reason, int limit)
        {
            finalOmission = Omission(reason, limit, Size(finalChecks));
            finalChecks = Array.Empty<NetworkVerificationCheckInfo>();
        }

        for (var i = 0; i < effects.Length; i++)
            if (effects[i].Effect is not null && Size(effects[i].Effect) > maxItemChars)
                OmitEffect(i, StructuredOperationBatchPayloadBudget.ItemLimitReason, maxItemChars);
        for (var i = 0; i < (operations?.Length ?? 0); i++)
            if (operations![i].Evidence is not null && Size(operations[i].Evidence) > maxItemChars)
                OmitEvidence(i, StructuredOperationBatchPayloadBudget.ItemLimitReason, maxItemChars);
        if (finalChecks is { Count: > 0 } && Size(finalChecks) > maxItemChars)
            OmitFinal(StructuredOperationBatchPayloadBudget.ItemLimitReason, maxItemChars);

        if (Size(Present()) > maxDocumentChars)
        {
            // Remove diagnostics whole, never preserve a prefix of rejected worker text.
            current = Diagnostics(current, true, originalChars);
            // Drop whole root values, largest first, before asking the shared batch helper to
            // fit its results into the remaining COMPLETE document. Stable ties retain order.
            var candidates = effects.Select((e, i) => (Kind: 0, Index: i, Chars: e.Effect is null ? 0 : Size(e.Effect)))
                .Concat((operations ?? []).Select((o, i) => (Kind: 1, Index: i, Chars: o.Evidence is null ? 0 : Size(o.Evidence))))
                .Append((Kind: 2, Index: 0, Chars: finalChecks is { Count: > 0 } ? Size(finalChecks) : 0))
                .Where(c => c.Chars > 0).OrderByDescending(c => c.Chars).ThenBy(c => c.Kind).ThenBy(c => c.Index);
            foreach (var candidate in candidates)
            {
                if (Size(Present()) <= maxDocumentChars) break;
                if (candidate.Kind == 0) OmitEffect(candidate.Index, StructuredOperationBatchPayloadBudget.DocumentLimitReason, maxDocumentChars);
                else if (candidate.Kind == 1) OmitEvidence(candidate.Index, StructuredOperationBatchPayloadBudget.DocumentLimitReason, maxDocumentChars);
                else OmitFinal(StructuredOperationBatchPayloadBudget.DocumentLimitReason, maxDocumentChars);
            }
        }

        if (current.Batch is not { } batch) return Present();
        // The generic helper's omitted status describes delivery. Network keeps the trusted
        // execution status and reports incomplete delivery through omission and root success.
        StructuredOperationBatch Restore(StructuredOperationBatch candidate) => StructuredOperationBatch.FromItems(
            candidate.Operations.Select((item, index) => item with { Status = batch.Operations[index].Status }).ToArray(), candidate.Truncation);
        var bounded = StructuredOperationBatchPayloadBudget.Apply(batch, candidate => Present(Restore(candidate)),
            "network_read", _ => Guidance, maxItemChars, maxDocumentChars);
        return Present(Restore(bounded));
    }

    /// <summary>
    /// Admission reserves the actual worst compact wire shape before any binding/worker activity.
    /// Default JSON escaping, all ID copies, 2 guards/item plus a partial-write guard, every
    /// omission and the batch truncation are included. Unbounded selectors live only in omitted
    /// values; they never enter this protected core. Reads retain their existing admission rules.
    /// </summary>
    internal static int MeasureProtectedCore(IReadOnlyList<NetworkOperationRequest> items)
    {
        var omission = Omission(StructuredOperationBatchPayloadBudget.DocumentLimitReason, int.MaxValue, int.MaxValue);
        var longestId = items.OrderByDescending(i => Size(i.OperationId)).First().OperationId;
        var guards = items.SelectMany(item => Enumerable.Repeat(new WriteGuardReport(
            new string('x', 64), "block", item.OperationId, DiagnosticSummary, null), 2))
            .Append(new(new string('x', 64), "block", longestId, DiagnosticSummary, null)).ToArray();
        var batch = new StructuredOperationBatch(items.Count,
            new(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue),
            items.Select(item => new StructuredOperationItem(item.OperationId, item.Operation, "succeeded", null,
                new(new string('x', 64), DiagnosticSummary), omission, StructuredOperationSkipReasons.EarlierOperationFailed,
                Array.Empty<string>())).ToArray(),
            new(true, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, items.Select(i => i.OperationId).ToArray()));
        return Size(new NetworkGuardedWriteResponse("network_write", "1.0", "applied", false,
            new(new string('x', 64), DiagnosticSummary), Array.Empty<string>(), guards,
            items.Select(item => new NetworkWriteEffectPresentation(item.OperationId, null, omission)).ToArray(), batch,
            new(false, items.Select(item => new NetworkOperationVerification(item.OperationId, item.Operation,
                "not_required", null, omission)).ToArray(), Array.Empty<NetworkVerificationCheckInfo>(), omission), omission));
    }

    private static NetworkGuardedWriteResponse Diagnostics(NetworkGuardedWriteResponse response, bool compact, int originalChars)
    {
        var omitted = false;
        string Message(string message)
        {
            if ((!compact && Size(message) <= DiagnosticChars) || message == DiagnosticSummary) return message;
            omitted = true;
            return DiagnosticSummary;
        }
        IReadOnlyList<string> Warnings(IReadOnlyList<string> warnings)
        {
            if (warnings.Count == 0 || (!compact && Size(warnings) <= DiagnosticChars)) return warnings;
            omitted = true;
            return Array.Empty<string>();
        }
        var error = response.Error is null ? null : response.Error with { Message = Message(response.Error.Message) };
        var warnings = Warnings(response.Warnings);
        var guards = response.Guards.Select(guard => guard with { Message = Message(guard.Message) }).ToArray();
        var batch = response.Batch is null ? null : StructuredOperationBatch.FromItems(response.Batch.Operations.Select(item => item with
        {
            Failure = item.Failure is null ? null : item.Failure with { Message = Message(item.Failure.Message) },
            Warnings = Warnings(item.Warnings)
        }).ToArray(), response.Batch.Truncation);
        return response with { Error = error, Warnings = warnings, Guards = guards, Batch = batch,
            Omission = omitted ? Omission("diagnosticDetailsOmitted", compact ? StructuredOperationBatchPayloadBudget.MaxDocumentChars : DiagnosticChars, originalChars) : response.Omission };
    }

    private static bool HasOmission(NetworkGuardedWriteResponse response) => response.Omission is not null
        || response.Effects.Any(e => e.Omission is not null)
        || response.Batch?.Operations.Any(i => i.Omission is not null) == true
        || response.Verification?.Omission is not null
        || response.Verification?.Operations.Any(o => o.Omission is not null) == true;
    private static StructuredOperationOmission Omission(string reason, int limit, int originalChars)
        => new(reason, limit, originalChars, "network_read", Guidance);
    private static int Size<T>(T value) => CanonicalJson.Serialize(value).Length;
}
