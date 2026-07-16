namespace TiaMcpServer.Batch;

/// <summary>Per-item lifecycle status used across batch previews and applies.</summary>
public static class BatchOperationStatus
{
    public const string Pending = "pending";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

/// <summary>Immutable result for a single batch item.</summary>
public sealed record BatchOperationResult(
    string OperationId,
    string Operation,
    string Status,
    string? Result,
    IReadOnlyList<string>? Warnings = null);
