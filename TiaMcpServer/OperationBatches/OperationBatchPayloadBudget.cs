namespace TiaMcpServer.OperationBatches;

public static class OperationBatchPayloadBudget
{
    public const int MaxItemChars = 60_000;
    public const int MaxBatchChars = 180_000;

    public static IReadOnlyList<OperationBatchResult> Apply(
        IReadOnlyList<OperationBatchResult> results,
        string toolName,
        string retryToolName,
        string narrowingHint)
        => Apply(results, toolName, retryToolName, narrowingHint, MaxItemChars, MaxBatchChars);

    public static IReadOnlyList<OperationBatchResult> Apply(
        IReadOnlyList<OperationBatchResult> results,
        string toolName,
        string retryToolName,
        string narrowingHint,
        int maxItemChars,
        int maxBatchChars)
    {
        var budgeted = new List<OperationBatchResult>(results.Count);

        foreach (var item in results)
        {
            var candidate = BoundItem(item, maxItemChars, narrowingHint);
            if (FitsFinalReadResponse(budgeted, candidate, toolName, maxBatchChars))
            {
                budgeted.Add(candidate);
                continue;
            }

            if (string.Equals(candidate.Status, OperationBatchStatus.Failed, StringComparison.Ordinal))
            {
                PreserveFailureByOmittingPriorSuccessfulPayloads(
                    budgeted, candidate, toolName, retryToolName, narrowingHint, maxItemChars, maxBatchChars);
                PreserveFailureByTruncatingPriorFailureDetails(budgeted, candidate, toolName, maxItemChars, maxBatchChars);
                PreserveFailureByTruncatingPriorWarnings(budgeted, candidate, toolName, maxItemChars, maxBatchChars);
                if (!FitsFinalReadResponse(budgeted, candidate, toolName, maxBatchChars))
                {
                    candidate = TruncateFailureDetail(candidate, maxItemChars);
                }

                if (!FitsFinalReadResponse(budgeted, candidate, toolName, maxBatchChars))
                {
                    candidate = TruncateFailureWarnings(candidate, maxItemChars);
                }

                if (!FitsFinalReadResponse(budgeted, candidate, toolName, maxBatchChars))
                {
                    throw new InvalidOperationException(
                        "The batch budget is too small to represent every failed operation status.");
                }

                budgeted.Add(candidate);
                continue;
            }

            var omission = Omit(item, retryToolName, narrowingHint, maxItemChars, maxBatchChars);
            if (!FitsFinalReadResponse(budgeted, omission, toolName, maxBatchChars))
            {
                omission = CompactOmission(omission, maxItemChars);
            }

            budgeted.Add(omission);
        }

        return budgeted;
    }

    private static OperationBatchResult BoundItem(
        OperationBatchResult item,
        int maxItemChars,
        string narrowingHint)
    {
        var text = item.Result ?? string.Empty;
        if (text.Length <= maxItemChars)
        {
            return item;
        }

        var trailer = TruncationTrailer(maxItemChars, narrowingHint);
        var retainedLength = Math.Max(0, maxItemChars - trailer.Length);
        return item with { Result = text.Substring(0, retainedLength) + trailer };
    }

    private static void PreserveFailureByOmittingPriorSuccessfulPayloads(
        List<OperationBatchResult> budgeted,
        OperationBatchResult failed,
        string toolName,
        string retryToolName,
        string narrowingHint,
        int maxItemChars,
        int maxBatchChars)
    {
        for (var index = 0;
             index < budgeted.Count && !FitsFinalReadResponse(budgeted, failed, toolName, maxBatchChars);
             index++)
        {
            if (string.Equals(budgeted[index].Status, OperationBatchStatus.Failed, StringComparison.Ordinal))
            {
                continue;
            }

            var omission = Omit(budgeted[index], retryToolName, narrowingHint, maxItemChars, maxBatchChars);
            budgeted[index] = omission;
            if (!FitsFinalReadResponse(budgeted, failed, toolName, maxBatchChars))
            {
                budgeted[index] = CompactOmission(omission, maxItemChars);
            }
        }
    }

    private static void PreserveFailureByTruncatingPriorFailureDetails(
        List<OperationBatchResult> budgeted,
        OperationBatchResult failed,
        string toolName,
        int maxItemChars,
        int maxBatchChars)
    {
        for (var index = 0;
             index < budgeted.Count && !FitsFinalReadResponse(budgeted, failed, toolName, maxBatchChars);
             index++)
        {
            if (!string.Equals(budgeted[index].Status, OperationBatchStatus.Failed, StringComparison.Ordinal))
            {
                continue;
            }

            budgeted[index] = TruncateFailureDetail(budgeted[index], maxItemChars);
        }
    }

    private static OperationBatchResult TruncateFailureDetail(OperationBatchResult item, int maxItemChars)
        => item with { Result = LimitMarker("[FAILED DETAIL TRUNCATED]", maxItemChars, string.Empty) };

    private static void PreserveFailureByTruncatingPriorWarnings(
        List<OperationBatchResult> budgeted,
        OperationBatchResult failed,
        string toolName,
        int maxItemChars,
        int maxBatchChars)
    {
        for (var index = 0;
             index < budgeted.Count && !FitsFinalReadResponse(budgeted, failed, toolName, maxBatchChars);
             index++)
        {
            if (!string.Equals(budgeted[index].Status, OperationBatchStatus.Failed, StringComparison.Ordinal))
            {
                continue;
            }

            budgeted[index] = TruncateFailureWarnings(budgeted[index], maxItemChars);
        }
    }

    private static OperationBatchResult TruncateFailureWarnings(OperationBatchResult item, int maxItemChars)
    {
        if (item.Warnings is null || item.Warnings.Count == 0)
        {
            return item;
        }

        return item with
        {
            Warnings = new[] { LimitMarker("[FAILED WARNINGS TRUNCATED]", maxItemChars, string.Empty) }
        };
    }

    private static OperationBatchResult Omit(
        OperationBatchResult item,
        string retryToolName,
        string narrowingHint,
        int maxItemChars,
        int maxBatchChars)
        => item with
        {
            Status = OperationBatchStatus.Omitted,
            Result = LimitMarker(
                OmissionMarker(retryToolName, narrowingHint, maxBatchChars),
                maxItemChars,
                "[OMITTED]"),
            Warnings = Array.Empty<string>()
        };

    private static OperationBatchResult CompactOmission(OperationBatchResult item, int maxItemChars)
        => item with { Result = LimitMarker("[OMITTED]", maxItemChars, string.Empty) };

    private static bool FitsFinalReadResponse(
        IReadOnlyList<OperationBatchResult> budgeted,
        OperationBatchResult candidate,
        string toolName,
        int maxBatchChars)
    {
        var results = new List<OperationBatchResult>(budgeted.Count + 1);
        results.AddRange(budgeted);
        results.Add(candidate);
        return OperationBatchResultFormatter.Read(toolName, results).Length <= maxBatchChars;
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

    public static string TruncationTrailer(int maxItemChars, string narrowingHint)
    {
        var fullTrailer = $"\n[TRUNCATED — this item's payload exceeded {maxItemChars} characters. {narrowingHint}]";
        return LimitMarker(fullTrailer, maxItemChars, $"[TRUNCATED: exceeded {maxItemChars} chars]");
    }

    public static string OmissionMarker(string retryToolName, string narrowingHint, int maxBatchChars)
        => $"[OMITTED — the combined batch response exceeded {maxBatchChars} characters. "
            + $"Re-run this operationId in its own {retryToolName} call, narrowed with {narrowingHint}]";
}
