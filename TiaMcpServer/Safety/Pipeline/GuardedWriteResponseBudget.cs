using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Safety.Pipeline;

/// <summary>Budgets delivery copies without changing execution or verification truth.</summary>
internal sealed class GuardedWriteResponseBudget(string retryTool, string guidance, string diagnosticSummary)
{
    internal static int Size<T>(T value) => CanonicalJson.Serialize(value).Length;

    internal StructuredOperationOmission Omission(string reason, int limit, int originalChars)
        => new(reason, limit, originalChars, retryTool, guidance);

    internal TResponse Apply<TResponse>(Func<StructuredOperationBatch?, TResponse> present,
        Func<StructuredOperationBatch?> batch, IEnumerable<GuardedWriteBudgetRoot> itemRoots,
        Func<IEnumerable<GuardedWriteBudgetRoot>> documentRoots, int maxItemChars, int maxDocumentChars,
        Action? beforeDocumentOmission = null)
    {
        // Enumerate lazily: omitting a PLC diff changes the following whole-effect measurement.
        foreach (var root in itemRoots)
            if (root.Chars > maxItemChars)
                root.Omit(StructuredOperationBatchPayloadBudget.ItemLimitReason, maxItemChars);

        if (Size(present(null)) > maxDocumentChars)
        {
            beforeDocumentOmission?.Invoke();
            foreach (var root in documentRoots())
            {
                if (Size(present(null)) <= maxDocumentChars) break;
                if (root.Chars > 0) root.Omit(StructuredOperationBatchPayloadBudget.DocumentLimitReason, maxDocumentChars);
            }
        }

        return batch() is { } trusted
            ? present(ApplyBatch(trusted, candidate => present(candidate), maxItemChars, maxDocumentChars))
            : present(null);
    }

    internal StructuredOperationBatch ApplyBatch<TResponse>(StructuredOperationBatch trusted,
        Func<StructuredOperationBatch, TResponse> present, int maxItemChars, int maxDocumentChars)
    {
        StructuredOperationBatch Restore(StructuredOperationBatch candidate) => StructuredOperationBatch.FromItems(
            candidate.Operations.Select((item, index) => item with { Status = trusted.Operations[index].Status }).ToArray(), candidate.Truncation);
        return Restore(StructuredOperationBatchPayloadBudget.Apply(trusted, candidate => present(Restore(candidate)),
            retryTool, _ => guidance, maxItemChars, maxDocumentChars));
    }

    internal static bool HasOmission(StructuredOperationOmission? root, IEnumerable<StructuredOperationOmission?> effects,
        StructuredOperationBatch? batch, StructuredOperationOmission? verification,
        IEnumerable<StructuredOperationOmission?>? operationChecks = null)
        => root is not null || effects.Any(o => o is not null)
            || batch?.Operations.Any(i => i.Omission is not null) == true || verification is not null
            || operationChecks?.Any(o => o is not null) == true;

    internal static bool OversizedBatchOrDiagnostics(WriteToolError? error, IReadOnlyList<string> warnings,
        IReadOnlyList<WriteGuardReport> guards, StructuredOperationBatch? batch, int limit)
        => error is not null && Size(error.Message) > limit || Size(warnings) > limit
            || guards.Any(g => Size(g.Message) > limit)
            || batch?.Operations.Any(i => i.Result is not null && Size(i.Result) > limit
                || i.Failure is not null && Size(i.Failure.Message) > limit || Size(i.Warnings) > limit) == true;

    internal int MeasureProtectedCore<TResponse>(IReadOnlyList<(string Id, string Operation)> items,
        int guardsPerItem, int trailingGuards, bool includeWarnings, Func<GuardedWriteProtectedCore, TResponse> present)
    {
        if (items.Count == 0) return 0;
        var omission = Omission(StructuredOperationBatchPayloadBudget.DocumentLimitReason, int.MaxValue, int.MaxValue);
        var category = new string('x', 64);
        var longest = items.OrderByDescending(i => Size(i.Id)).First().Id;
        var guards = items.SelectMany(i => Enumerable.Repeat(new WriteGuardReport(category, "block", i.Id, diagnosticSummary, null), guardsPerItem))
            .Concat(Enumerable.Repeat(new WriteGuardReport(category, "block", longest, diagnosticSummary, null), trailingGuards)).ToArray();
        string[] warnings = includeWarnings ? [diagnosticSummary] : [];
        var batch = new StructuredOperationBatch(items.Count, new(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue),
            items.Select(i => new StructuredOperationItem(i.Id, i.Operation, OperationBatchStatus.Succeeded, null,
                new(category, diagnosticSummary), omission, StructuredOperationSkipReasons.EarlierOperationFailed, warnings)).ToArray(),
            new(true, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, items.Select(i => i.Id).ToArray()));
        return Size(present(new(new(category, diagnosticSummary), warnings, guards, batch, omission)));
    }

    internal GuardedWriteDiagnosticCompactor Diagnostics(bool omitWarnings = false, int? messageLimit = null)
        => new(diagnosticSummary, omitWarnings, messageLimit);

    internal StructuredOperationOmission? DiagnosticOmission(GuardedWriteDiagnosticCompactor diagnostics,
        StructuredOperationOmission? existing, int limit, int originalChars)
        => diagnostics.Omitted ? Omission(StructuredOperationBatchPayloadBudget.DiagnosticOmissionReason, limit, originalChars) : existing;
}

internal readonly record struct GuardedWriteBudgetRoot(int Chars, Action<string, int> Omit);

internal sealed record GuardedWriteProtectedCore(WriteToolError Error, IReadOnlyList<string> Warnings,
    IReadOnlyList<WriteGuardReport> Guards, StructuredOperationBatch Batch, StructuredOperationOmission Omission);

internal sealed class GuardedWriteDiagnosticCompactor(string summary, bool omitWarnings, int? messageLimit)
{
    internal bool Omitted { get; private set; }

    internal string Message(string message)
    {
        if (message == summary || messageLimit is { } limit && GuardedWriteResponseBudget.Size(message) <= limit) return message;
        Omitted = true;
        return summary;
    }

    internal IReadOnlyList<string> Warnings(IReadOnlyList<string> warnings)
    {
        if (!omitWarnings) return warnings.Select(Message).Distinct(StringComparer.Ordinal).ToArray();
        if (warnings.Count == 0 || messageLimit is { } limit && GuardedWriteResponseBudget.Size(warnings) <= limit) return warnings;
        Omitted = true;
        return [];
    }

    internal WriteToolError? Error(WriteToolError? error) => error is null ? null : error with { Message = Message(error.Message) };

    internal IReadOnlyList<WriteGuardReport> Guards(IReadOnlyList<WriteGuardReport> guards)
        => guards.Select(g => g with { Message = Message(g.Message) }).ToArray();

    internal StructuredOperationBatch? Batch(StructuredOperationBatch? batch,
        Func<StructuredOperationItem, JsonElement?>? rewriteResult = null)
        => batch is null ? null : StructuredOperationBatch.FromItems(batch.Operations.Select(item => item with
        {
            Result = rewriteResult is null ? item.Result : rewriteResult(item),
            Failure = item.Failure is null ? null : item.Failure with { Message = Message(item.Failure.Message) },
            Warnings = Warnings(item.Warnings),
        }).ToArray(), batch.Truncation);
}
