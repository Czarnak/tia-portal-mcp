using System.Text;
using TiaMcpServer.Contracts.Block;

namespace TiaMcpServer.Plc;

/// <summary>Resolves unique text edits against one unchanged block export.</summary>
internal static class PlcBlockLogicPatch
{
    private const int DelimiterPrefixLength = 9; // "--- FILE:"
    private const string MissingXml = "This bundle contains no Simatic ML (.xml) document. The block could not be exported as Simatic ML, which usually means it is inconsistent — compile it in TIA Portal and read it again before writing.";

    internal static string Apply(string format, string currentExport, IReadOnlyList<PlcTextEdit> edits)
    {
        var original = Scope(format, currentExport);
        if (edits.Count == 0)
            throw new ArgumentException("At least one patch edit is required.", nameof(edits));

        var (normalized, boundaries) = NormalizeWithBoundaries(original);
        var spans = new List<EditSpan>(edits.Count);
        for (var index = 0; index < edits.Count; index++)
        {
            var edit = edits[index];
            if (string.IsNullOrWhiteSpace(edit.OldText))
                throw new ArgumentException($"edits[{index}].oldText must be nonblank.", nameof(edits));
            if (BlockBundleText.ContainsDelimiterLine(edit.NewText))
                throw new ArgumentException($"edits[{index}].newText contains a block bundle delimiter line.", nameof(edits));

            var anchor = NormalizeLineEndings(edit.OldText);
            var start = normalized.IndexOf(anchor, StringComparison.Ordinal);
            if (start < 0)
                throw new ArgumentException($"edits[{index}].oldText matched zero times.", nameof(edits));
            if (normalized.IndexOf(anchor, start + 1, StringComparison.Ordinal) >= 0)
                throw new ArgumentException($"edits[{index}].oldText matched several times.", nameof(edits));

            var originalStart = boundaries[start];
            var originalEnd = boundaries[start + anchor.Length];
            var terminator = ReplacementTerminator(original, originalStart, originalEnd);
            var replacement = NormalizeLineEndings(edit.NewText).Replace("\n", terminator, StringComparison.Ordinal);
            spans.Add(new EditSpan(originalStart, originalEnd, replacement, index));
        }

        spans.Sort((left, right) => left.Start.CompareTo(right.Start));
        for (var index = 1; index < spans.Count; index++)
        {
            if (spans[index].Start < spans[index - 1].End)
                throw new ArgumentException($"edits[{spans[index - 1].Index}] and edits[{spans[index].Index}] overlap.", nameof(edits));
        }

        return Assemble(original, spans);
    }

    private static string Scope(string format, string currentExport)
    {
        if (!string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal))
            return currentExport;

        try
        {
            var bundle = BlockBundleText.Parse("current.xml", currentExport);
            return BlockBundleText.FindSimaticMlDocument(bundle)?.Content
                ?? throw new ArgumentException(MissingXml, nameof(currentExport));
        }
        catch (BlockBundleFormatException exception)
        {
            throw new ArgumentException(exception.Message, nameof(currentExport), exception);
        }
    }

    private static (string Text, int[] Boundaries) NormalizeWithBoundaries(string original)
    {
        var text = new StringBuilder(original.Length);
        var boundaries = new List<int>(original.Length + 1) { 0 };
        for (var index = 0; index < original.Length; index++)
        {
            if (original[index] == '\r' && index + 1 < original.Length && original[index + 1] == '\n')
            {
                text.Append('\n');
                index++;
            }
            else
            {
                text.Append(original[index]);
            }
            boundaries.Add(index + 1);
        }

        return (text.ToString(), boundaries.ToArray());
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string ReplacementTerminator(string original, int start, int end)
    {
        var crlf = false;
        var lf = false;
        for (var index = start; index < end; index++)
        {
            if (original[index] != '\n')
                continue;
            if (index > start && original[index - 1] == '\r')
                crlf = true;
            else
                lf = true;
        }

        if (crlf != lf)
            return crlf ? "\r\n" : "\n";

        var lineEnd = original.IndexOf('\n', start);
        return lineEnd < 0 || lineEnd > 0 && original[lineEnd - 1] == '\r' ? "\r\n" : "\n";
    }

    private static string Assemble(string original, IReadOnlyList<EditSpan> spans)
    {
        var output = new StringBuilder(original.Length);
        var unchanged = new List<OriginalSpan>(spans.Count + 1);
        var applied = new List<AppliedEdit>(spans.Count);
        var offset = 0;
        foreach (var span in spans)
        {
            AppendOriginal(span.Start);
            applied.Add(new AppliedEdit(output.Length, span.Replacement.Length, span.Index));
            output.Append(span.Replacement);
            offset = span.End;
        }
        AppendOriginal(original.Length);

        var result = output.ToString();
        ValidateIntroducedDelimiters(original, result, unchanged, applied);
        return result;

        void AppendOriginal(int end)
        {
            if (end == offset)
                return;
            unchanged.Add(new OriginalSpan(output.Length, offset, end - offset));
            output.Append(original, offset, end - offset);
        }
    }

    private static void ValidateIntroducedDelimiters(
        string original, string result, IReadOnlyList<OriginalSpan> unchanged, IReadOnlyList<AppliedEdit> applied)
    {
        if (!BlockBundleText.ContainsDelimiterLine(result))
            return;

        var originalSpanIndex = 0;
        for (var lineStart = 0; lineStart < result.Length;)
        {
            var lineEnd = result.IndexOf('\n', lineStart);
            if (lineEnd < 0)
                lineEnd = result.Length;

            if (BlockBundleText.ContainsDelimiterLine(result.Substring(lineStart, lineEnd - lineStart)))
            {
                while (originalSpanIndex < unchanged.Count
                    && unchanged[originalSpanIndex].OutputStart + unchanged[originalSpanIndex].Length <= lineStart)
                    originalSpanIndex++;

                if (!InheritedPrefix(lineStart))
                {
                    var edit = applied.FirstOrDefault(candidate => candidate.OutputStart <= lineStart + DelimiterPrefixLength
                        && candidate.OutputStart + candidate.Length >= lineStart - 1);
                    var subject = edit is null ? "The patch" : $"edits[{edit.Index}]";
                    throw new ArgumentException($"{subject} introduces a block bundle delimiter line.", "edits");
                }
            }
            lineStart = lineEnd + 1;
        }

        bool InheritedPrefix(int outputStart)
        {
            if (originalSpanIndex >= unchanged.Count)
                return false;
            var span = unchanged[originalSpanIndex];
            if (outputStart < span.OutputStart || outputStart + DelimiterPrefixLength > span.OutputStart + span.Length)
                return false;
            var originalStart = span.OriginalStart + outputStart - span.OutputStart;
            return originalStart == 0 || original[originalStart - 1] == '\n';
        }
    }

    private sealed record EditSpan(int Start, int End, string Replacement, int Index);
    private sealed record OriginalSpan(int OutputStart, int OriginalStart, int Length);
    private sealed record AppliedEdit(int OutputStart, int Length, int Index);
}
