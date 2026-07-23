using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class BlockImportStager
{
    public static IReadOnlyList<string> StageDocuments(
        string stagingRoot,
        ParsedBlockImportBundle bundle)
    {
        if (bundle is null) throw new ArgumentNullException(nameof(bundle));

        var canonicalRoot = CanonicalizeRoot(stagingRoot);
        var stagedPaths = new List<string>(bundle.Documents.Count);

        foreach (var document in bundle.Documents)
        {
            var stagedPath = Path.GetFullPath(Path.Combine(canonicalRoot, document.SafeFileName));
            if (!IsDirectChildOfRoot(canonicalRoot, stagedPath))
            {
                throw ValidationFailure("Block import document path must remain directly under the staging root.");
            }

            if (File.Exists(stagedPath))
            {
                throw ValidationFailure("Block import staging destination already exists.");
            }

            File.WriteAllText(stagedPath, document.Content, Encoding.UTF8);
            if (!File.Exists(stagedPath))
            {
                throw new IOException("Block import staging did not create the expected document.");
            }

            stagedPaths.Add(stagedPath);
        }

        return stagedPaths.AsReadOnly();
    }

    private static WorkerOperationException ValidationFailure(string message)
    {
        return new WorkerOperationException(WorkerFailureCategories.ValidationError, message);
    }

    private static string CanonicalizeRoot(string stagingRoot)
    {
        if (string.IsNullOrWhiteSpace(stagingRoot))
        {
            throw new ArgumentException("Block import staging root is required.", nameof(stagingRoot));
        }

        var canonicalRoot = Path.GetFullPath(stagingRoot);
        var root = Path.GetPathRoot(canonicalRoot);
        if (!string.Equals(canonicalRoot, root, StringComparison.OrdinalIgnoreCase))
        {
            canonicalRoot = canonicalRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return canonicalRoot;
    }

    private static bool IsDirectChildOfRoot(string canonicalRoot, string candidatePath)
    {
        return string.Equals(
            Path.GetDirectoryName(candidatePath),
            canonicalRoot,
            StringComparison.OrdinalIgnoreCase);
    }
}
