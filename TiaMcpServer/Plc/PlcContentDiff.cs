using System.Security.Cryptography;
using System.Text;

namespace TiaMcpServer.Plc;

/// <summary>
/// Bounded evidence of what a content write would change. Bounds: 40 lines and 8,192 characters per
/// side, 512 per line, 320 lines and 32,768 characters across the calls sharing one budget.
/// </summary>
public static class PlcContentDiff
{
    public const int MaxExcerptLinesPerSide = 40;
    public const int MaxExcerptCharsPerSide = 8_192;
    public const int MaxExcerptCharsPerLine = 512;
    public const int MaxCallExcerptLines = 320;
    public const int MaxCallExcerptChars = 32_768;

    /// <summary>
    /// Compares the current and requested document text. Pass one <paramref name="budget"/> to the
    /// calls of a single plan so their excerpts together stay inside the per-call bound; a diff
    /// past the bound keeps its hashes and counts but carries empty excerpts.
    /// </summary>
    public static PlcContentDiffEvidence Build(string current, string requested, PlcContentDiffBudget? budget = null)
    {
        budget ??= new PlcContentDiffBudget();
        var compared = Compare(current, requested);
        var currentExcerpt = BuildExcerpt(
            compared.CurrentLines,
            compared.FirstChangedCurrentLineIndex,
            compared.LastChangedCurrentLineIndex);
        var requestedExcerpt = BuildExcerpt(
            compared.RequestedLines,
            compared.FirstChangedRequestedLineIndex,
            compared.LastChangedRequestedLineIndex);

        var excerptLines = currentExcerpt.Lines.Count + requestedExcerpt.Lines.Count;
        var excerptChars = CountExcerptCharacters(currentExcerpt) + CountExcerptCharacters(requestedExcerpt);
        var exhausted = budget.Exhausted
            || excerptLines > budget.RemainingLines
            || excerptChars > budget.RemainingChars;
        if (exhausted)
        {
            budget.Exhausted = true;
            currentExcerpt = EmptyExcerpt(compared.CurrentLines, compared.FirstChangedCurrentLineIndex, compared.LastChangedCurrentLineIndex);
            requestedExcerpt = EmptyExcerpt(compared.RequestedLines, compared.FirstChangedRequestedLineIndex, compared.LastChangedRequestedLineIndex);
        }
        else
        {
            budget.RemainingLines -= excerptLines;
            budget.RemainingChars -= excerptChars;
        }

        return new PlcContentDiffEvidence(
            new PlcContentDiffSide(Sha256(current), current.Length, CountLines(current), currentExcerpt),
            new PlcContentDiffSide(Sha256(requested), requested.Length, CountLines(requested), requestedExcerpt),
            compared.RawTextEqual,
            compared.NormalizedLinesEqual,
            compared.LineEndingOnly,
            compared.UnchangedPrefixLineCount,
            compared.UnchangedSuffixLineCount,
            compared.CurrentChangedLineCount,
            compared.RequestedChangedLineCount,
            exhausted);
    }
    private static ComparedLines Compare(string currentText, string requestedText)
    {
        var currentLines = NormalizeLineEndings(currentText).Split('\n');
        var requestedLines = NormalizeLineEndings(requestedText).Split('\n');
        var prefix = 0;
        var commonLength = Math.Min(currentLines.Length, requestedLines.Length);
        while (prefix < commonLength && string.Equals(currentLines[prefix], requestedLines[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < currentLines.Length - prefix
            && suffix < requestedLines.Length - prefix
            && string.Equals(
                currentLines[currentLines.Length - 1 - suffix],
                requestedLines[requestedLines.Length - 1 - suffix],
                StringComparison.Ordinal))
        {
            suffix++;
        }

        var normalizedLinesEqual = prefix == currentLines.Length && prefix == requestedLines.Length;
        var currentChangedLineCount = currentLines.Length - prefix - suffix;
        var requestedChangedLineCount = requestedLines.Length - prefix - suffix;
        return new ComparedLines(
            currentLines,
            requestedLines,
            string.Equals(currentText, requestedText, StringComparison.Ordinal),
            normalizedLinesEqual,
            !string.Equals(currentText, requestedText, StringComparison.Ordinal) && normalizedLinesEqual,
            prefix,
            suffix,
            currentChangedLineCount,
            requestedChangedLineCount,
            currentChangedLineCount == 0 ? -1 : prefix,
            currentChangedLineCount == 0 ? -1 : currentLines.Length - suffix - 1,
            requestedChangedLineCount == 0 ? -1 : prefix,
            requestedChangedLineCount == 0 ? -1 : requestedLines.Length - suffix - 1);
    }

    private static PlcContentDiffExcerpt BuildExcerpt(string[] lines, int firstChangedLineIndex, int lastChangedLineIndex)
    {
        if (firstChangedLineIndex < 0)
        {
            return new PlcContentDiffExcerpt(Array.Empty<PlcContentDiffLine>(), 0, 0, false);
        }

        var selectedIndexes = SelectExcerptLineIndexes(firstChangedLineIndex, lastChangedLineIndex);
        var charactersPerLine = selectedIndexes.Count == 0
            ? 0
            : Math.Min(MaxExcerptCharsPerLine, MaxExcerptCharsPerSide / selectedIndexes.Count);
        var selected = selectedIndexes
            .Select(index => ToExcerptLine(lines[index], index, charactersPerLine))
            .ToArray();
        var selectedIndexesSet = selectedIndexes.ToHashSet();
        var omittedLineCount = 0;
        var omittedCharacterCount = 0;
        for (var index = firstChangedLineIndex; index <= lastChangedLineIndex; index++)
        {
            if (!selectedIndexesSet.Contains(index))
            {
                omittedLineCount++;
                omittedCharacterCount += lines[index].Length;
            }
        }

        omittedCharacterCount += selected.Sum(line => line.OmittedCharacterCount);
        return new PlcContentDiffExcerpt(selected, omittedLineCount, omittedCharacterCount, false);
    }

    private static PlcContentDiffExcerpt EmptyExcerpt(string[] lines, int firstChangedLineIndex, int lastChangedLineIndex)
    {
        if (firstChangedLineIndex < 0)
        {
            return new PlcContentDiffExcerpt(Array.Empty<PlcContentDiffLine>(), 0, 0, true);
        }

        var omittedCharacterCount = 0;
        for (var index = firstChangedLineIndex; index <= lastChangedLineIndex; index++)
        {
            omittedCharacterCount += lines[index].Length;
        }

        return new PlcContentDiffExcerpt(
            Array.Empty<PlcContentDiffLine>(),
            lastChangedLineIndex - firstChangedLineIndex + 1,
            omittedCharacterCount,
            true);
    }

    private static IReadOnlyList<int> SelectExcerptLineIndexes(int firstChangedLineIndex, int lastChangedLineIndex)
    {
        var changedLineCount = lastChangedLineIndex - firstChangedLineIndex + 1;
        if (changedLineCount <= MaxExcerptLinesPerSide)
        {
            return Enumerable.Range(firstChangedLineIndex, changedLineCount).ToArray();
        }

        var firstCount = MaxExcerptLinesPerSide / 2;
        return Enumerable.Range(firstChangedLineIndex, firstCount)
            .Concat(Enumerable.Range(lastChangedLineIndex - firstCount + 1, firstCount))
            .ToArray();
    }

    private static PlcContentDiffLine ToExcerptLine(string text, int zeroBasedLineNumber, int charactersPerLine)
    {
        var retainedCharacterCount = Math.Min(text.Length, charactersPerLine);
        return new PlcContentDiffLine(
            zeroBasedLineNumber + 1,
            text[..retainedCharacterCount],
            text.Length - retainedCharacterCount);
    }

    private static int CountExcerptCharacters(PlcContentDiffExcerpt excerpt)
        => excerpt.Lines.Sum(line => line.Text.Length);

    private static int CountLines(string text)
        => NormalizeLineEndings(text).Split('\n').Length;

    private static string NormalizeLineEndings(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string Sha256(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private sealed record ComparedLines(
        string[] CurrentLines,
        string[] RequestedLines,
        bool RawTextEqual,
        bool NormalizedLinesEqual,
        bool LineEndingOnly,
        int UnchangedPrefixLineCount,
        int UnchangedSuffixLineCount,
        int CurrentChangedLineCount,
        int RequestedChangedLineCount,
        int FirstChangedCurrentLineIndex,
        int LastChangedCurrentLineIndex,
        int FirstChangedRequestedLineIndex,
        int LastChangedRequestedLineIndex);
}

/// <summary>Remaining excerpt allowance shared by the diffs of one call.</summary>
public sealed class PlcContentDiffBudget
{
    public int RemainingLines { get; set; } = PlcContentDiff.MaxCallExcerptLines;

    public int RemainingChars { get; set; } = PlcContentDiff.MaxCallExcerptChars;

    public bool Exhausted { get; set; }
}

public sealed record PlcContentDiffEvidence(
    PlcContentDiffSide Current,
    PlcContentDiffSide Requested,
    bool RawTextEqual,
    bool NormalizedLinesEqual,
    bool LineEndingOnly,
    int UnchangedPrefixLineCount,
    int UnchangedSuffixLineCount,
    int CurrentChangedLineCount,
    int RequestedChangedLineCount,
    bool BudgetExhausted);

public sealed record PlcContentDiffSide(string Sha256, int CharacterCount, int LineCount, PlcContentDiffExcerpt Excerpt);

public sealed record PlcContentDiffExcerpt(
    IReadOnlyList<PlcContentDiffLine> Lines,
    int OmittedLineCount,
    int OmittedCharacterCount,
    bool BudgetExhausted);

public sealed record PlcContentDiffLine(int LineNumber, string Text, int OmittedCharacterCount);
