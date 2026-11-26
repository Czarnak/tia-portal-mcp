using System.Text;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Contracts.Block;

/// <summary>
/// Sole owner of the multi-document block bundle format returned by get_block_content and
/// consumed by update_block_logic. Both directions live here so the producer cannot drift
/// from the consumer, and so host and worker read a bundle the same way. Siemens-free; refusals
/// throw <see cref="BlockBundleFormatException"/>.
/// </summary>
public static class BlockBundleText
{
    private static readonly Regex DocumentDelimiter = new(
        @"^--- FILE: (?<name>.+) ---(?:\r?\n|$)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex DocumentDelimiterCandidate = new(
        @"^--- FILE:",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex ReservedDeviceName = new(
        @"^(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
            throw new BlockBundleFormatException("A block bundle must contain at least one document.");
        }

        var builder = new StringBuilder();

        for (var index = 0; index < documents.Count; index++)
        {
            var document = documents[index];

            if (ContainsDelimiterLine(document.Content))
            {
                throw new BlockBundleFormatException(
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

    /// <summary>
    /// Splits <paramref name="rawContent"/> into its documents. Content without any delimiter is
    /// one document named <paramref name="documentName"/>.
    /// </summary>
    public static ParsedBlockImportBundle Parse(string documentName, string rawContent)
    {
        ValidateDocumentName(documentName);

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            throw new BlockBundleFormatException("Block import document content is required.");
        }

        ValidateDelimiterLines(rawContent);
        var delimiters = DocumentDelimiter.Matches(rawContent);
        if (delimiters.Count == 0)
        {
            return CreateBundle(new[] { CreateDocument(documentName, rawContent) });
        }

        if (!string.IsNullOrWhiteSpace(rawContent.Substring(0, delimiters[0].Index)))
        {
            throw new BlockBundleFormatException("Block import bundle contains undeclared content.");
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
                throw new BlockBundleFormatException("Every declared block import document must have content.");
            }

            if (!safeNames.Add(name))
            {
                throw new BlockBundleFormatException("Block import document names must be unique.");
            }

            documents.Add(CreateDocument(name, content));
        }

        return CreateBundle(documents);
    }

    /// <summary>The first Simatic ML (<c>.xml</c>, any case) document of the bundle, or null.</summary>
    public static BlockImportDocument? FindSimaticMlDocument(ParsedBlockImportBundle bundle)
    {
        if (bundle is null) throw new ArgumentNullException(nameof(bundle));
        foreach (var document in bundle.Documents)
        {
            if (document.LogicalName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                return document;
            }
        }

        return null;
    }

    private static bool EndsWithNewline(string content)
    {
        return content.Length > 0 && content[content.Length - 1] == '\n';
    }

    private static ParsedBlockImportBundle CreateBundle(IReadOnlyList<BlockImportDocument> documents)
    {
        return new ParsedBlockImportBundle(documents[0].LogicalName, documents);
    }

    private static BlockImportDocument CreateDocument(string name, string content)
    {
        return new BlockImportDocument(name, name, content);
    }

    private static void ValidateDelimiterLines(string rawContent)
    {
        foreach (Match candidate in DocumentDelimiterCandidate.Matches(rawContent))
        {
            var delimiter = DocumentDelimiter.Match(rawContent, candidate.Index);
            if (!delimiter.Success || delimiter.Index != candidate.Index)
            {
                throw new BlockBundleFormatException("Block import bundle delimiter is invalid.");
            }
        }
    }

    private static void ValidateDocumentName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            Path.IsPathRooted(name) ||
            !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) ||
            name == "." ||
            name == ".." ||
            name.EndsWith(".", StringComparison.Ordinal) ||
            name.EndsWith(" ", StringComparison.Ordinal) ||
            name.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\' }) >= 0 ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            ReservedDeviceName.IsMatch(name))
        {
            throw new BlockBundleFormatException("Block import document name is invalid.");
        }
    }
}
