using TiaMcpServer.Contracts.Block;

namespace TiaMcpServer.Plc;

/// <summary>Result of a get_block_content / get_type_content read. ContentHash is null for withDependencies reads.</summary>
public sealed record PlcContentResult(
    string Format,
    string Content,
    string? ContentHash,
    IReadOnlyList<string> Warnings);

/// <summary>Result of a completed update_block_logic: the normalized write format and the worker's typed import evidence.</summary>
public sealed record PlcBlockImportResult(string Format, BlockImportOutcomeInfo ImportOutcome);
