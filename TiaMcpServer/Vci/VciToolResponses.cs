using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Vci;

public sealed record VciToolError(string Category, string Message);

public sealed record VciReadResponse(string Tool, string ContractVersion, bool Success,
    VciToolError? Error, IReadOnlyList<string> Warnings, StructuredOperationBatch? Batch);
