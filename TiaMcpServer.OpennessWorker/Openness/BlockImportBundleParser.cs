using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class BlockImportBundleParser
{
    private static readonly Regex DocumentDelimiter = new(
        @"^--- FILE: (?<name>.+) ---(?:\r?\n|$)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static ParsedBlockImportBundle Parse(string documentName, string rawContent)
    {
        ValidateDocumentName(documentName);

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            throw ValidationFailure("Block import document content is required.");
        }

        var delimiters = DocumentDelimiter.Matches(rawContent);
        if (delimiters.Count == 0)
        {
            return CreateBundle(new[] { CreateDocument(documentName, rawContent) });
        }

        if (!string.IsNullOrWhiteSpace(rawContent.Substring(0, delimiters[0].Index)))
        {
            throw ValidationFailure("Block import bundle contains undeclared content.");
        }

        var documents = new List<BlockImportDocument>(delimiters.Count);
        var safeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < delimiters.Count; index++)
        {
            var delimiter = delimiters[index];
            var contentStart = delimiter.Index + delimiter.Length;
            var contentEnd = index + 1 < delimiters.Count
                ? delimiters[index + 1].Index
                : rawContent.Length;
            var content = rawContent.Substring(contentStart, contentEnd - contentStart);
            var name = delimiter.Groups["name"].Value;

            ValidateDocumentName(name);
            if (string.IsNullOrWhiteSpace(content))
            {
                throw ValidationFailure("Every declared block import document must have content.");
            }

            if (!safeNames.Add(name))
            {
                throw ValidationFailure("Block import document names must be unique.");
            }

            documents.Add(CreateDocument(name, content));
        }

        return CreateBundle(documents);
    }

    private static ParsedBlockImportBundle CreateBundle(IReadOnlyList<BlockImportDocument> documents)
    {
        return new ParsedBlockImportBundle(documents[0].LogicalName, documents);
    }

    private static BlockImportDocument CreateDocument(string name, string content)
    {
        return new BlockImportDocument(name, name, content);
    }

    private static void ValidateDocumentName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            Path.IsPathRooted(name) ||
            !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) ||
            name == "." ||
            name == ".." ||
            name.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\' }) >= 0 ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw ValidationFailure("Block import document name is invalid.");
        }
    }

    private static WorkerOperationException ValidationFailure(string message)
    {
        return new WorkerOperationException(WorkerFailureCategories.ValidationError, message);
    }
}
