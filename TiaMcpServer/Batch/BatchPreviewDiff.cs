using System.Security.Cryptography;
using System.Text;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Batch;

public static class BatchPreviewDiff
{
    public const int MaxExcerptLinesPerSide = 40;
    public const int MaxExcerptCharsPerSide = 8_192;
    public const int MaxExcerptCharsPerLine = 512;
    public const int MaxBatchExcerptLines = 320;
    public const int MaxBatchExcerptChars = 32_768;

    public static BatchPreviewDiffDocument? Build(
        IReadOnlyList<BatchOperationRequest> operations,
        IReadOnlyList<OperationBatchCurrentState> states)
    {
        if (operations.Count != states.Count)
        {
            throw new ArgumentException("Operations and current-state rows must align by index.");
        }

        var remainingBatchLines = MaxBatchExcerptLines;
        var remainingBatchChars = MaxBatchExcerptChars;
        var entries = new List<BatchPreviewDiffEntry>();

        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            if (!IsEligible(operation, out var requestedText, out var normalizedFormat))
            {
                continue;
            }

            var currentText = states[index].CurrentState;
            var compared = Compare(currentText, requestedText);
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
            var batchBudgetExhausted = excerptLines > remainingBatchLines || excerptChars > remainingBatchChars;
            if (batchBudgetExhausted)
            {
                currentExcerpt = EmptyExcerpt(compared.CurrentLines, compared.FirstChangedCurrentLineIndex, compared.LastChangedCurrentLineIndex);
                requestedExcerpt = EmptyExcerpt(compared.RequestedLines, compared.FirstChangedRequestedLineIndex, compared.LastChangedRequestedLineIndex);
            }
            else
            {
                remainingBatchLines -= excerptLines;
                remainingBatchChars -= excerptChars;
            }

            entries.Add(new BatchPreviewDiffEntry(
                operation.OperationId,
                operation.Operation,
                normalizedFormat,
                new BatchPreviewDiffSide(Sha256(currentText), currentText.Length, CountLines(currentText), currentExcerpt),
                new BatchPreviewDiffSide(Sha256(requestedText), requestedText.Length, CountLines(requestedText), requestedExcerpt),
                compared.RawTextEqual,
                compared.NormalizedLinesEqual,
                compared.LineEndingOnly,
                compared.UnchangedPrefixLineCount,
                compared.UnchangedSuffixLineCount,
                compared.CurrentChangedLineCount,
                compared.RequestedChangedLineCount,
                batchBudgetExhausted));
        }

        return entries.Count == 0 ? null : new BatchPreviewDiffDocument(entries);
    }

    private static bool IsEligible(BatchOperationRequest operation, out string requestedText, out string normalizedFormat)
    {
        switch (operation.Operation)
        {
            case "update_block_logic":
                requestedText = operation.YamlContent ?? string.Empty;
                normalizedFormat = NormalizeFormat(operation.Format, SourceFormatNames.Xml);
                return true;
            case "update_type_content":
                requestedText = operation.SourceContent ?? string.Empty;
                normalizedFormat = NormalizeFormat(operation.Format, SourceFormatNames.Source);
                return true;
            default:
                requestedText = string.Empty;
                normalizedFormat = string.Empty;
                return false;
        }
    }

    private static string NormalizeFormat(string? format, string defaultFormat)
        => string.IsNullOrWhiteSpace(format)
            ? defaultFormat
            : string.Equals(format, SourceFormatNames.Xml, StringComparison.OrdinalIgnoreCase)
                ? SourceFormatNames.Xml
                : SourceFormatNames.Source;

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

    private static BatchPreviewDiffExcerpt BuildExcerpt(string[] lines, int firstChangedLineIndex, int lastChangedLineIndex)
    {
        if (firstChangedLineIndex < 0)
        {
            return new BatchPreviewDiffExcerpt(Array.Empty<BatchPreviewDiffLine>(), 0, 0, false);
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
        return new BatchPreviewDiffExcerpt(selected, omittedLineCount, omittedCharacterCount, false);
    }

    private static BatchPreviewDiffExcerpt EmptyExcerpt(string[] lines, int firstChangedLineIndex, int lastChangedLineIndex)
    {
        if (firstChangedLineIndex < 0)
        {
            return new BatchPreviewDiffExcerpt(Array.Empty<BatchPreviewDiffLine>(), 0, 0, true);
        }

        var omittedCharacterCount = 0;
        for (var index = firstChangedLineIndex; index <= lastChangedLineIndex; index++)
        {
            omittedCharacterCount += lines[index].Length;
        }

        return new BatchPreviewDiffExcerpt(
            Array.Empty<BatchPreviewDiffLine>(),
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

    private static BatchPreviewDiffLine ToExcerptLine(string text, int zeroBasedLineNumber, int charactersPerLine)
    {
        var retainedCharacterCount = Math.Min(text.Length, charactersPerLine);
        return new BatchPreviewDiffLine(
            zeroBasedLineNumber + 1,
            text[..retainedCharacterCount],
            text.Length - retainedCharacterCount);
    }

    private static int CountExcerptCharacters(BatchPreviewDiffExcerpt excerpt)
        => excerpt.Lines.Sum(line => line.Text.Length);

    private static int CountLines(string text)
        => NormalizeLineEndings(text).Split('\n').Length;

    private static string NormalizeLineEndings(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string Sha256(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

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

public sealed record BatchPreviewDiffDocument(IReadOnlyList<BatchPreviewDiffEntry> Operations);

public sealed record BatchPreviewDiffEntry(
    string OperationId,
    string Operation,
    string Format,
    BatchPreviewDiffSide Current,
    BatchPreviewDiffSide Requested,
    bool RawTextEqual,
    bool NormalizedLinesEqual,
    bool LineEndingOnly,
    int UnchangedPrefixLineCount,
    int UnchangedSuffixLineCount,
    int CurrentChangedLineCount,
    int RequestedChangedLineCount,
    bool BatchBudgetExhausted);

public sealed record BatchPreviewDiffSide(string Sha256, int CharacterCount, int LineCount, BatchPreviewDiffExcerpt Excerpt);

public sealed record BatchPreviewDiffExcerpt(
    IReadOnlyList<BatchPreviewDiffLine> Lines,
    int OmittedLineCount,
    int OmittedCharacterCount,
    bool BudgetExhausted);

public sealed record BatchPreviewDiffLine(int LineNumber, string Text, int OmittedCharacterCount);
