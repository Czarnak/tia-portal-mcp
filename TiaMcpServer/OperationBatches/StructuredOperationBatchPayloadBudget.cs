using TiaMcpServer.Json;

namespace TiaMcpServer.OperationBatches;

/// <summary>
/// Bounds a structured batch so the response a caller receives stays inside a character budget.
///
/// <para>
/// Two rules make this different from text truncation. First, values are dropped WHOLE: an
/// oversized result is replaced by an omission carrying retry guidance, never cut mid-value, so
/// the response can never contain a half-written JSON document. Second, the budget is measured
/// against the exact canonical response document the caller receives — envelope, counts and
/// truncation record included — not against an unrelated serialization of one payload.
/// </para>
///
/// <para>
/// Evidence has a priority order. Successful results are the first thing dropped, because they can
/// be fetched again; complete warning entries go next; a failure message is shortened only as a
/// last resort, and a failure's category and status are never dropped at all. Whatever was changed
/// is reported in <see cref="StructuredBatchTruncation"/> so the caller can see what it is missing.
/// </para>
/// </summary>
public static class StructuredOperationBatchPayloadBudget
{
    public const int MaxItemChars = 60_000;

    public const int MaxDocumentChars = 180_000;

    /// <summary>One result on its own exceeded the per-item character limit.</summary>
    public const string ItemLimitReason = "resultExceededItemCharLimit";

    /// <summary>The results fit individually, but the complete response document did not.</summary>
    public const string DocumentLimitReason = "responseExceededDocumentCharLimit";

    private const string ShortenedMessageMarker = " [message shortened]";

    /// <summary>How much of a failure message survives, tried in order until the document fits.</summary>
    private static readonly int[] RetainedFailureMessageChars = { 120, 0 };

    /// <summary>
    /// <see cref="StructuredBatchTruncation.PresentedChars"/> is part of the document it measures,
    /// so it is resolved by fixed point. Its digit count only shrinks, so this converges quickly.
    /// </summary>
    private const int PresentedCharsFixedPointAttempts = 5;

    /// <param name="compose">Wraps a batch in the caller's declared response type. The budget
    /// measures what this produces, so the envelope is inside the budget too.</param>
    /// <param name="retryTool">Tool an agent should call to fetch an omitted result.</param>
    /// <param name="guidance">Per-operation advice for narrowing a retry.</param>
    public static StructuredOperationBatch Apply<TResponse>(
        StructuredOperationBatch batch,
        Func<StructuredOperationBatch, TResponse> compose,
        string retryTool,
        Func<StructuredOperationItem, string> guidance,
        int maxItemChars = MaxItemChars,
        int maxDocumentChars = MaxDocumentChars)
    {
        var items = batch.Operations.ToList();
        var originalChars = Measure(compose, StructuredOperationBatch.FromItems(items));
        var affected = new List<string>();
        var omittedResults = 0;
        var omittedWarnings = 0;
        var changed = false;

        StructuredBatchTruncation? Draft() => changed
            ? new StructuredBatchTruncation(
                Truncated: true,
                originalChars,
                // A conservative width placeholder: no real value can be wider than the limit.
                PresentedChars: maxDocumentChars,
                omittedResults,
                omittedWarnings,
                affected.ToArray())
            : null;

        // Counts and statuses are re-derived on every measurement, so the projected document is
        // always the document that would actually be returned at that moment.
        StructuredOperationBatch Project() => StructuredOperationBatch.FromItems(items, Draft());

        bool Fits() => Measure(compose, Project()) <= maxDocumentChars;

        void Record(string operationId)
        {
            changed = true;
            if (!affected.Contains(operationId, StringComparer.Ordinal))
            {
                affected.Add(operationId);
            }
        }

        // 1-2. Per-result budget: an oversized result is replaced as a whole.
        for (var index = 0; index < items.Count; index++)
        {
            var resultChars = ResultChars(items[index]);
            if (resultChars <= maxItemChars)
            {
                continue;
            }

            items[index] = Omit(items[index], ItemLimitReason, maxItemChars, resultChars, retryTool, guidance);
            omittedResults++;
            Record(items[index].OperationId);
        }

        // 3. Document budget: drop complete successful results, largest first so the fewest
        // operations lose their data, ties broken by request order so the choice is deterministic.
        var candidates = items
            .Select((item, index) => (Index: index, Chars: ResultChars(item)))
            .Where(candidate => candidate.Chars > 0)
            .OrderByDescending(candidate => candidate.Chars)
            .ThenBy(candidate => candidate.Index)
            .ToArray();

        foreach (var (index, resultChars) in candidates)
        {
            if (Fits())
            {
                break;
            }

            items[index] = Omit(
                items[index], DocumentLimitReason, maxDocumentChars, resultChars, retryTool, guidance);
            omittedResults++;
            Record(items[index].OperationId);
        }

        // 4. Complete warning entries go before any failure evidence is touched.
        for (var index = 0; index < items.Count && !Fits(); index++)
        {
            if (items[index].Warnings.Count == 0)
            {
                continue;
            }

            omittedWarnings += items[index].Warnings.Count;
            items[index] = items[index] with { Warnings = Array.Empty<string>() };
            Record(items[index].OperationId);
        }

        // 5. Last resort: shorten failure messages. Category and status always survive.
        foreach (var retained in RetainedFailureMessageChars)
        {
            for (var index = 0; index < items.Count && !Fits(); index++)
            {
                var shortened = Shorten(items[index], retained);
                if (shortened is null)
                {
                    continue;
                }

                items[index] = shortened;
                Record(items[index].OperationId);
            }
        }

        if (!Fits())
        {
            throw new InvalidOperationException(
                $"A {maxDocumentChars}-character budget is too small to report every operation status.");
        }

        return changed
            ? StructuredOperationBatch.FromItems(items, ResolvePresentedChars(compose, items, Draft()!))
            : batch;
    }

    private static StructuredBatchTruncation ResolvePresentedChars<TResponse>(
        Func<StructuredOperationBatch, TResponse> compose,
        IReadOnlyList<StructuredOperationItem> items,
        StructuredBatchTruncation truncation)
    {
        for (var attempt = 0; attempt < PresentedCharsFixedPointAttempts; attempt++)
        {
            var measured = Measure(compose, StructuredOperationBatch.FromItems(items, truncation));
            if (measured == truncation.PresentedChars)
            {
                break;
            }

            truncation = truncation with { PresentedChars = measured };
        }

        return truncation;
    }

    private static int Measure<TResponse>(
        Func<StructuredOperationBatch, TResponse> compose,
        StructuredOperationBatch batch)
        => CanonicalJson.Serialize(compose(batch)).Length;

    /// <summary>Canonical length of an item's result, or 0 when it has none to drop.</summary>
    private static int ResultChars(StructuredOperationItem item)
        => item.Result is { } result ? CanonicalJson.Serialize(result).Length : 0;

    private static StructuredOperationItem Omit(
        StructuredOperationItem item,
        string reason,
        int limitChars,
        int originalChars,
        string retryTool,
        Func<StructuredOperationItem, string> guidance)
        => item with
        {
            Status = OperationBatchStatus.Omitted,
            Result = null,
            Omission = new StructuredOperationOmission(
                reason,
                limitChars,
                originalChars,
                retryTool,
                guidance(item)),
        };

    /// <summary>Shortens one failure message, or returns null when there is nothing left to gain.</summary>
    private static StructuredOperationItem? Shorten(StructuredOperationItem item, int retainedChars)
    {
        if (item.Failure is not { } failure)
        {
            return null;
        }

        var shortened = failure.Message.Length <= retainedChars
            ? failure.Message
            : failure.Message[..retainedChars] + ShortenedMessageMarker;

        return shortened.Length < failure.Message.Length
            ? item with { Failure = failure with { Message = shortened } }
            : null;
    }
}
