using System.Text.Json;

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

        foreach (var item in results)
        {
            var candidate = BoundItem(item, maxItemChars);
            if (FitsFinalReadResponse(budgeted, candidate, maxBatchChars))
            {
                budgeted.Add(candidate);
                continue;
            }

            if (string.Equals(candidate.Status, BatchOperationStatus.Failed, StringComparison.Ordinal))
            {
                PreserveFailureByOmittingPriorSuccessfulPayloads(budgeted, candidate, maxItemChars, maxBatchChars);
                budgeted.Add(candidate);
                continue;
            }

            var omission = Omit(item, maxItemChars, maxBatchChars);
            if (!FitsFinalReadResponse(budgeted, omission, maxBatchChars))
            {
                omission = CompactOmission(omission, maxItemChars);
            }

            budgeted.Add(omission);
        }

        return budgeted;
    }

    private static BatchOperationResult BoundItem(BatchOperationResult item, int maxItemChars)
    {
        var text = item.Result ?? string.Empty;
        if (text.Length <= maxItemChars)
        {
            return item;
        }

        var trailer = TruncationTrailer(maxItemChars);
        var retainedLength = Math.Max(0, maxItemChars - trailer.Length);
        return item with { Result = text.Substring(0, retainedLength) + trailer };
    }

    private static void PreserveFailureByOmittingPriorSuccessfulPayloads(
        List<BatchOperationResult> budgeted,
        BatchOperationResult failed,
        int maxItemChars,
        int maxBatchChars)
    {
        for (var index = 0; index < budgeted.Count && !FitsFinalReadResponse(budgeted, failed, maxBatchChars); index++)
        {
            if (string.Equals(budgeted[index].Status, BatchOperationStatus.Failed, StringComparison.Ordinal))
            {
                continue;
            }

            var omission = Omit(budgeted[index], maxItemChars, maxBatchChars);
            budgeted[index] = omission;
            if (!FitsFinalReadResponse(budgeted, failed, maxBatchChars))
            {
                budgeted[index] = CompactOmission(omission, maxItemChars);
            }
        }
    }

    private static BatchOperationResult Omit(BatchOperationResult item, int maxItemChars, int maxBatchChars)
        => item with
        {
            Status = BatchOperationStatus.Omitted,
            Result = LimitMarker(OmissionMarker(maxBatchChars), maxItemChars, "[OMITTED]"),
            Warnings = Array.Empty<string>()
        };

    private static BatchOperationResult CompactOmission(BatchOperationResult item, int maxItemChars)
        => item with { Result = LimitMarker("[OMITTED]", maxItemChars, string.Empty) };

    private static bool FitsFinalReadResponse(
        IReadOnlyList<BatchOperationResult> budgeted,
        BatchOperationResult candidate,
        int maxBatchChars)
    {
        var results = new List<BatchOperationResult>(budgeted.Count + 1);
        results.AddRange(budgeted);
        results.Add(candidate);
        return ReadBatchResponseLength(results) <= maxBatchChars;
    }

    private static int ReadBatchResponseLength(IReadOnlyList<BatchOperationResult> results)
    {
        var failed = results.Count(result => string.Equals(result.Status, BatchOperationStatus.Failed, StringComparison.Ordinal));
        var omitted = results.Count(result => string.Equals(result.Status, BatchOperationStatus.Omitted, StringComparison.Ordinal));
        return JsonSerializer.Serialize(new
        {
            tool = "execute_read_batch",
            success = failed == 0 && omitted == 0,
            operationCount = results.Count,
            succeeded = results.Count(result => string.Equals(result.Status, BatchOperationStatus.Succeeded, StringComparison.Ordinal)),
            failed,
            omitted,
            operations = results.Select(result => new
            {
                operationId = result.OperationId,
                operation = result.Operation,
                status = result.Status,
                result = result.Result,
                warnings = result.Warnings
            })
        }).Length;
    }

    private static string LimitMarker(string marker, int maxItemChars, string minimumMarker)
    {
        if (marker.Length <= maxItemChars)
        {
            return marker;
        }

        return minimumMarker.Length <= maxItemChars
            ? minimumMarker
            : minimumMarker.Substring(0, maxItemChars);
    }

    public static string TruncationTrailer(int maxItemChars)
    {
        var fullTrailer = $"\n[TRUNCATED — this item's payload exceeded {maxItemChars} characters. "
            + "Narrow the read (plcName, filter, startPath, depth, maxResults) or split the batch.]";
        return LimitMarker(fullTrailer, maxItemChars, $"[TRUNCATED: exceeded {maxItemChars} chars]");
    }

    public static string OmissionMarker(int maxBatchChars)
        => $"[OMITTED — the combined batch response exceeded {maxBatchChars} characters. "
            + "Re-run this operationId in its own execute_read_batch call, narrowed with "
            + "plcName/filter/startPath/depth/maxResults.]";
}
