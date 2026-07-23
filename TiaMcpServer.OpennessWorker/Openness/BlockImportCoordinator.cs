using System;
using System.Collections.Generic;
using System.IO;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class BlockImportCoordinator
{
    public static BlockImportResult Execute(
        string documentName,
        string rawContent,
        Action<DirectoryInfo, string> importDocuments,
        Func<BlockPostconditionEvidence> verifyPostcondition,
        Action<string>? cleanupDirectory = null)
    {
        if (importDocuments is null) throw new ArgumentNullException(nameof(importDocuments));
        if (verifyPostcondition is null) throw new ArgumentNullException(nameof(verifyPostcondition));

        Action<string> deleteDirectory = cleanupDirectory
            ?? (path => Directory.Delete(path, recursive: true));

        var bundle = BlockImportBundleParser.Parse(documentName, rawContent);
        var warnings = new List<string>();
        Exception? primaryFailure = null;
        string? stagingPath = null;
        string? payload = null;

        try
        {
            stagingPath = Path.Combine(Path.GetTempPath(), "tia-mcp-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingPath);

            var stagedPaths = BlockImportStager.StageDocuments(stagingPath, bundle);
            VerifyStagedDocuments(stagingPath, bundle, stagedPaths);

            importDocuments(new DirectoryInfo(stagingPath), bundle.PrimaryDocumentName);

            var evidence = verifyPostcondition()
                ?? throw new InvalidOperationException("Block update verification did not return evidence.");
            AddWarnings(warnings, evidence.Warnings);
            BlockPostconditionVerifier.Verify(evidence);

            payload = "Import succeeded.";
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }
        finally
        {
            var cleanupPath = stagingPath;
            if (cleanupPath is not null && cleanupPath.Length > 0 && Directory.Exists(cleanupPath))
            {
                try
                {
                    deleteDirectory(cleanupPath);
                }
                catch (Exception exception)
                {
                    AddWarning(warnings, "Block import staging cleanup failed: " + exception.Message);
                }
            }
        }

        if (primaryFailure is WorkerOperationException workerFailure)
        {
            AddWarnings(warnings, workerFailure.Warnings);
            throw new WorkerOperationException(
                workerFailure.FailureCategory,
                workerFailure.Message,
                warnings);
        }

        if (primaryFailure is not null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                primaryFailure.Message,
                warnings);
        }

        return new BlockImportResult(
            payload ?? throw new InvalidOperationException("Block import did not produce a result."),
            warnings);
    }

    private static void VerifyStagedDocuments(
        string stagingPath,
        ParsedBlockImportBundle bundle,
        IReadOnlyList<string> stagedPaths)
    {
        if (stagedPaths.Count != bundle.Documents.Count)
        {
            throw new InvalidOperationException("Block import staging did not produce every declared document.");
        }

        for (var index = 0; index < bundle.Documents.Count; index++)
        {
            var expectedPath = Path.GetFullPath(Path.Combine(stagingPath, bundle.Documents[index].SafeFileName));
            if (!string.Equals(expectedPath, stagedPaths[index], StringComparison.OrdinalIgnoreCase)
                || !File.Exists(stagedPaths[index]))
            {
                throw new InvalidOperationException("Block import staging did not preserve the declared document order.");
            }
        }
    }

    private static void AddWarnings(List<string> destination, IReadOnlyList<string> source)
    {
        foreach (var warning in source)
        {
            AddWarning(destination, warning);
        }
    }

    private static void AddWarning(List<string> destination, string warning)
    {
        var capped = warning.Length <= 512 ? warning : warning.Substring(0, 512);
        if (!destination.Contains(capped))
        {
            destination.Add(capped);
        }
    }
}
