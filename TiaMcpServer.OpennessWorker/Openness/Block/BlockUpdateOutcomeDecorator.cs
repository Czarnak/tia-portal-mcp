using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

/// <summary>
/// Adds bounded block-import evidence to update failures that occur outside the importer. This
/// helper has no Siemens dependency so every pre-import boundary uses one exact projection.
/// </summary>
internal static class BlockUpdateOutcomeDecorator
{
    public static string? NormalizeFormatOrNull(string? format)
        => SourceFormatNames.TryNormalize(
            format,
            SourceFormatNames.Xml,
            out var normalized,
            out _)
            ? normalized
            : null;

    public static WorkerResponse Decorate(
        WorkerResponse failure,
        string? normalizedFormatOrNull,
        bool importerEntered)
    {
        if (failure.Success)
            return failure;

        failure.Error = BlockImportDiagnosticSanitizer.Failure(BlockImportDiagnosticContext.Import);
        failure.Warnings = ToList(BlockImportDiagnosticSanitizer.SanitizeWarnings(failure.Warnings));
        failure.BlockImportOutcome ??= Build(normalizedFormatOrNull, importerEntered);
        return failure;
    }

    public static WorkerResponse Decorate(
        WorkerOperationException exception,
        string? normalizedFormatOrNull,
        bool importerEntered)
        => new()
        {
            Success = false,
            FailureCategory = exception.FailureCategory,
            Error = BlockImportDiagnosticSanitizer.Failure(
                BlockImportDiagnosticContext.Import,
                exception),
            Warnings = ToList(BlockImportDiagnosticSanitizer.SanitizeWarnings(exception.Warnings)),
            BlockImportOutcome = exception.BlockImportOutcome
                ?? Build(normalizedFormatOrNull, importerEntered)
        };

    public static WorkerResponse Decorate(
        Exception exception,
        string? normalizedFormatOrNull,
        bool importerEntered)
        => Decorate(
            new WorkerResponse
            {
                Success = false,
                FailureCategory = WorkerFailureCategories.WorkerOperationFailed,
                Error = exception.Message
            },
            normalizedFormatOrNull,
            importerEntered);

    private static BlockImportOutcomeInfo Build(string? normalizedFormatOrNull, bool importerEntered)
        => importerEntered
            ? new BlockImportOutcomeInfo
            {
                ImportStage = "unknown",
                ImportResultState = "unavailable",
                TargetMutationCommitted = null,
                CompileStage = "unavailable",
                FinalReadStage = "unavailable",
                TemporarySourceState = normalizedFormatOrNull == SourceFormatNames.Xml
                    ? "not_applicable"
                    : "unknown"
            }
            : new BlockImportOutcomeInfo
            {
                ImportStage = "not_started",
                ImportResultState = "unavailable",
                TargetMutationCommitted = false,
                CompileStage = "not_started",
                FinalReadStage = "not_started",
                TemporarySourceState = normalizedFormatOrNull switch
                {
                    SourceFormatNames.Xml => "not_applicable",
                    SourceFormatNames.Source => "not_created",
                    _ => "unknown"
                }
            };

    private static List<string>? ToList(IReadOnlyList<string>? warnings)
        => warnings is { Count: > 0 } ? new List<string>(warnings) : null;
}
