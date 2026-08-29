using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OperationBatches;

/// <summary>Closed vocabulary of reasons an operation was never attempted.</summary>
public static class StructuredOperationSkipReasons
{
    /// <summary>Sequential apply stopped: an earlier operation in the same batch failed.</summary>
    public const string EarlierOperationFailed = "earlierOperationFailed";
}

/// <summary>Why one operation failed. <paramref name="Category"/> is a WorkerFailureCategories value.</summary>
public sealed record StructuredOperationFailure(string Category, string Message);

/// <summary>Why one operation's result was withheld, and how to retrieve it.</summary>
public sealed record StructuredOperationOmission(
    string Reason,
    int? LimitChars,
    int OriginalChars,
    string RetryTool,
    string Guidance,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StructuredOperationOmissionSubject? Subject = null);

/// <summary>Identifies the omitted result without changing existing omission wire shapes.</summary>
public sealed record StructuredOperationOmissionSubject(
    string Kind,
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Identifier);

/// <summary>
/// One operation's outcome. <see cref="Result"/> is always a detached <see cref="JsonElement"/>
/// when present — never a nested JSON string — so a consumer never has to parse twice.
/// </summary>
public sealed record StructuredOperationItem(
    string OperationId,
    string Operation,
    string Status,
    JsonElement? Result,
    StructuredOperationFailure? Failure,
    StructuredOperationOmission? Omission,
    string? SkipReason,
    IReadOnlyList<string> Warnings);

public sealed record StructuredOperationCounts(
    int Succeeded,
    int Failed,
    int Omitted,
    int Skipped);

public sealed record StructuredBatchTruncation(
    bool Truncated,
    int OriginalChars,
    int PresentedChars,
    int OmittedResultCount,
    int OmittedWarningCount,
    IReadOnlyList<string> AffectedOperationIds);

public sealed record StructuredOperationBatch(
    int OperationCount,
    StructuredOperationCounts Counts,
    IReadOnlyList<StructuredOperationItem> Operations,
    StructuredBatchTruncation? Truncation)
{
    /// <summary>
    /// Builds a batch whose counts are derived from the FINAL item statuses. Counting anywhere
    /// else would let a later transformation (omission, skipping) leave the totals describing a
    /// state the caller never receives.
    /// </summary>
    public static StructuredOperationBatch FromItems(
        IReadOnlyList<StructuredOperationItem> items,
        StructuredBatchTruncation? truncation = null)
        => new(
            items.Count,
            new StructuredOperationCounts(
                Count(items, OperationBatchStatus.Succeeded),
                Count(items, OperationBatchStatus.Failed),
                Count(items, OperationBatchStatus.Omitted),
                Count(items, OperationBatchStatus.Skipped)),
            items,
            truncation);

    /// <summary>
    /// True only when every operation completed and delivered its result. Host-side only: it is
    /// derived from <see cref="Counts"/>, so publishing it would add a redundant member the
    /// declared output schema does not describe.
    /// </summary>
    [JsonIgnore]
    public bool IsFullySuccessful
        => Counts.Failed == 0 && Counts.Omitted == 0 && Counts.Skipped == 0;

    private static int Count(IReadOnlyList<StructuredOperationItem> items, string status)
        => items.Count(item => string.Equals(item.Status, status, StringComparison.Ordinal));
}
