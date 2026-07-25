using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Sole owner of the multi-document block bundle format returned by get_block_content and
/// consumed by update_block_logic. Both directions live here so the producer cannot drift
/// from the consumer — which is exactly the defect this type was introduced to end.
/// </summary>
internal static class BlockBundleFormat
{
    internal static readonly Regex DocumentDelimiter = new Regex(
        @"^--- FILE: (?<name>.+) ---(?:\r?\n|$)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    internal static readonly Regex DocumentDelimiterCandidate = new Regex(
        @"^--- FILE:",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static bool ContainsDelimiterLine(string content)
    {
        return content != null && DocumentDelimiterCandidate.IsMatch(content);
    }

    /// <summary>
    /// Renders documents into the bundle format. Guarantees the invariant the parser relies
    /// on: every marker after the first is preceded by exactly one newline. Content that does
    /// not already end in a newline gets one appended, so Compose is stable under a parse
    /// round trip rather than byte-identical to its input.
    /// </summary>
    public static string Compose(IReadOnlyList<BlockImportDocument> documents)
    {
        if (documents is null) throw new ArgumentNullException(nameof(documents));
        if (documents.Count == 0)
        {
            throw ValidationFailure("A block bundle must contain at least one document.");
        }

        var builder = new StringBuilder();

        for (var index = 0; index < documents.Count; index++)
        {
            var document = documents[index];

            if (ContainsDelimiterLine(document.Content))
            {
                throw ValidationFailure(
                    "Block bundle content must not contain a line that parses as a document delimiter.");
            }

            builder.Append("--- FILE: ").Append(document.LogicalName).Append(" ---\n");
            builder.Append(document.Content);

            var isLast = index == documents.Count - 1;
            if (!isLast && !EndsWithNewline(document.Content))
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static bool EndsWithNewline(string content)
    {
        return content.Length > 0 && content[content.Length - 1] == '\n';
    }

    private static WorkerOperationException ValidationFailure(string message)
    {
        return new WorkerOperationException(WorkerFailureCategories.ValidationError, message);
    }
}