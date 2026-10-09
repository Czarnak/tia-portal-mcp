using TiaMcpServer.OperationBatches;
using TiaMcpServer.Contracts.Block;

namespace TiaMcpServer.Plc;

/// <summary>A tool-level failure that prevented the batch from running at all.</summary>
public sealed record PlcToolError(string Category, string Message);

/// <summary>Result of a get_block_content / get_type_content read. ContentHash is null for withDependencies reads.</summary>
public sealed record PlcContentResult(
    string Format,
    string Content,
    string? ContentHash,
    IReadOnlyList<string> Warnings);

/// <summary>Result of a completed update_block_logic: the normalized write format and the worker's typed import evidence.</summary>
public sealed record PlcBlockImportResult(string Format, BlockImportOutcomeInfo ImportOutcome);

/// <summary>
/// Declared output schema of <c>plc_read</c>. Exactly one of <see cref="Batch"/> and
/// <see cref="Error"/> is populated; <see cref="Error"/> only for a rejection before anything ran.
/// </summary>
public sealed record PlcReadResponse(
    string Tool,
    string ContractVersion,
    bool Success,
    PlcToolError? Error,
    IReadOnlyList<string> Warnings,
    StructuredOperationBatch? Batch);
