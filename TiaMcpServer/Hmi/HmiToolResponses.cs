using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Hmi;

/// <summary>A tool-level failure that prevented the batch from running at all.</summary>
public sealed record HmiToolError(string Category, string Message);

/// <summary>
/// Declared output schema of <c>hmi_read</c>. Exactly one of <see cref="Batch"/> and
/// <see cref="Error"/> is populated; <see cref="Error"/> only for a rejection before anything ran.
/// </summary>
public sealed record HmiReadResponse(
    string Tool,
    string ContractVersion,
    bool Success,
    HmiToolError? Error,
    IReadOnlyList<string> Warnings,
    StructuredOperationBatch? Batch);
