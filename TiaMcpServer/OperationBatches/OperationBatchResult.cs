namespace TiaMcpServer.OperationBatches;

public static class OperationBatchStatus
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
    public const string Omitted = "omitted";
}

public sealed record OperationBatchResult(
    string OperationId,
    string Operation,
    string Status,
    string? Result,
    IReadOnlyList<string>? Warnings = null);

public sealed record OperationBatchTarget(
    string OperationId,
    string Operation,
    string Summary);

public sealed record OperationBatchCurrentState(
    string OperationId,
    string Operation,
    string CurrentState);
