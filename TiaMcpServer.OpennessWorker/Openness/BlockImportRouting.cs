using System;
using System.Collections.Generic;
using System.IO;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal enum BlockImportRoute
{
    SimaticMl,
    SimaticSd,
}

/// <summary>
/// Chooses which Openness importer a parsed bundle belongs to.
///
/// This decision is deliberately isolated and directly tested. It previously lived inline in
/// BlockImporter (commit c53e6f4) and was removed by an unrelated refactor (dddf9d2) without
/// any test failing, which left update_block_logic broken for months.
/// </summary>
internal static class BlockImportRouting
{
    public static BlockImportRoute SelectRoute(ParsedBlockImportBundle bundle)
    {
        if (bundle is null) throw new ArgumentNullException(nameof(bundle));
        return FindSimaticMlDocument(bundle) is null
            ? BlockImportRoute.SimaticSd
            : BlockImportRoute.SimaticMl;
    }

    public static BlockImportDocument SelectAuthoritativeDocument(ParsedBlockImportBundle bundle)
    {
        return FindSimaticMlDocument(bundle)
            ?? throw ValidationFailure(
                "This bundle contains no Simatic ML (.xml) document. The block could not be "
                + "exported as Simatic ML, which usually means it is inconsistent — compile it "
                + "in TIA Portal and read it again before writing.");
    }

    public static string SimaticSdBaseName(ParsedBlockImportBundle bundle)
    {
        if (bundle is null) throw new ArgumentNullException(nameof(bundle));

        // ImportFromDocuments resolves the document set by file name WITHOUT extension.
        return Path.GetFileNameWithoutExtension(bundle.PrimaryDocumentName);
    }

    /// <summary>
    /// Only the authoritative document is applied. If the caller edited any other document we
    /// must refuse rather than silently discard their edit.
    /// </summary>
    public static void EnsureOnlyAuthoritativeDocumentChanged(
        ParsedBlockImportBundle submitted,
        ParsedBlockImportBundle current,
        string authoritativeName)
    {
        if (submitted is null) throw new ArgumentNullException(nameof(submitted));
        if (current is null) throw new ArgumentNullException(nameof(current));

        var currentByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in current.Documents)
        {
            currentByName[document.LogicalName] = document.Content;
        }

        foreach (var document in submitted.Documents)
        {
            if (string.Equals(document.LogicalName, authoritativeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!currentByName.TryGetValue(document.LogicalName, out var currentContent)
                || !string.Equals(currentContent, document.Content, StringComparison.Ordinal))
            {
                throw ValidationFailure(
                    $"'{document.LogicalName}' was modified, but only '{authoritativeName}' is "
                    + "applied by update_block_logic. Re-read the block, edit "
                    + $"'{authoritativeName}', and submit again.");
            }
        }
    }

    private static BlockImportDocument? FindSimaticMlDocument(ParsedBlockImportBundle bundle)
    {
        foreach (var document in bundle.Documents)
        {
            if (document.LogicalName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                return document;
            }
        }

        return null;
    }

    private static WorkerOperationException ValidationFailure(string message)
    {
        return new WorkerOperationException(WorkerFailureCategories.ValidationError, message);
    }
}