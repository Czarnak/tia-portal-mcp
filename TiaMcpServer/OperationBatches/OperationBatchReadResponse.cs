namespace TiaMcpServer.OperationBatches;

/// <summary>A tool-level failure that prevented a read batch from running.</summary>
public sealed record OperationBatchReadError(string Category, string Message);

/// <summary>Shared read envelope. Exactly one of Batch and Error is populated.</summary>
public sealed record OperationBatchReadResponse(
    string Tool,
    string ContractVersion,
    bool Success,
    OperationBatchReadError? Error,
    IReadOnlyList<string> Warnings,
    StructuredOperationBatch? Batch);
