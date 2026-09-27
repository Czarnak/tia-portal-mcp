using System;
using System.Collections.Generic;

namespace TiaMcpServer.OpennessWorker.Openness;

internal enum BlockImportDiagnosticContext
{
    Import,
    Generation,
    SourceNodeCreation,
    SourceNodeDeletion,
    StagingCleanup,
    Verification
}

internal static class BlockImportDiagnosticSanitizer
{
    private const string GenericWarning = "Block import reported additional sanitized diagnostic information.";

    public static string Failure(BlockImportDiagnosticContext context, Exception? exception = null)
    {
        if (exception is WorkerOperationException workerFailure
            && IsClosedFailureSummary(workerFailure.Message))
            return workerFailure.Message;

        return context switch
        {
            BlockImportDiagnosticContext.Import => "Block import did not complete.",
            BlockImportDiagnosticContext.Generation => "Block source generation did not complete.",
            BlockImportDiagnosticContext.SourceNodeCreation => "Temporary source creation did not complete.",
            BlockImportDiagnosticContext.SourceNodeDeletion => "Temporary source cleanup could not be confirmed.",
            BlockImportDiagnosticContext.StagingCleanup => "Block import staging cleanup could not be confirmed.",
            _ => "Block update verification did not complete."
        };
    }

    public static string GeneratedCountWarning(int generatedCount) =>
        "TIA Portal reported " + generatedCount + " generated block object"
        + (generatedCount == 1 ? "." : "s.");

    public static string SourceNodeCleanupWarning() =>
        "Temporary source cleanup could not be confirmed; a project node may remain.";

    public static string StagingCleanupWarning() =>
        "Block import staging cleanup could not be confirmed.";

    public static IReadOnlyList<string> SanitizeWarnings(IReadOnlyList<string>? warnings)
    {
        if (warnings is null || warnings.Count == 0)
            return Array.Empty<string>();

        var result = new List<string>();
        foreach (var warning in warnings)
        {
            var sanitized = IsControlledWarning(warning) ? warning : GenericWarning;
            if (sanitized.Length > 256)
                sanitized = sanitized.Substring(0, 256);
            if (!result.Contains(sanitized))
                result.Add(sanitized);
        }
        return result.AsReadOnly();
    }

    private static bool IsControlledWarning(string? warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
            return false;
        return warning == SourceNodeCleanupWarning()
            || warning == StagingCleanupWarning()
            || warning == "Project state may have changed; inspect the project before retrying."
            || (warning!.StartsWith("TIA Portal reported ", StringComparison.Ordinal)
                && (warning.EndsWith(" generated block object.", StringComparison.Ordinal)
                    || warning.EndsWith(" generated block objects.", StringComparison.Ordinal)));
    }

    private static bool IsClosedFailureSummary(string message)
    {
        foreach (BlockImportDiagnosticContext context in Enum.GetValues(typeof(BlockImportDiagnosticContext)))
        {
            if (string.Equals(message, Failure(context), StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
