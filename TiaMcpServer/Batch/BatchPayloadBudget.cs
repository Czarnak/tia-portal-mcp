namespace TiaMcpServer.Batch;

/// <summary>
/// Host-side backstop that keeps execute_read_batch responses bounded no matter what the
/// caller asked for: each item's payload is capped, and once the whole batch exceeds its
/// budget, remaining oversized payloads are replaced with an explicit omission marker.
/// Pure and unit-testable; never mutates its input.
/// </summary>
public static class BatchPayloadBudget
{
    public const int MaxItemChars = 60_000;
    public const int MaxBatchChars = 180_000;

    public static IReadOnlyList<BatchOperationResult> Apply(IReadOnlyList<BatchOperationResult> results)
        => Apply(results, MaxItemChars, MaxBatchChars);

    public static IReadOnlyList<BatchOperationResult> Apply(
        IReadOnlyList<BatchOperationResult> results,
        int maxItemChars,
        int maxBatchChars)
    {
        var budgeted = new List<BatchOperationResult>(results.Count);
        var used = 0;

        foreach (var item in results)
        {
            var text = item.Result ?? string.Empty;
            var truncated = false;
            if (text.Length > maxItemChars)
            {
                text = text.Substring(0, maxItemChars) + TruncationTrailer(maxItemChars);
                truncated = true;
            }

            if (used + text.Length > maxBatchChars)
            {
                budgeted.Add(item with
                {
                    Status = BatchOperationStatus.Omitted,
                    Result = OmissionMarker(maxBatchChars)
                });
                continue;
            }

            budgeted.Add(truncated ? item with { Result = text } : item);
            used += text.Length;
        }

        return budgeted;
    }

    public static string TruncationTrailer(int maxItemChars)
        => $"\n[TRUNCATED — this item's payload exceeded {maxItemChars} characters. "
            + "Narrow the read (plcName, filter, startPath, depth, maxResults) or split the batch.]";

    public static string OmissionMarker(int maxBatchChars)
        => $"[OMITTED — the combined batch response exceeded {maxBatchChars} characters. "
            + "Re-run this operationId in its own execute_read_batch call, narrowed with "
            + "plcName/filter/startPath/depth/maxResults.]";
}
