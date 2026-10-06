using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;

namespace TiaMcpServer.Batch;

public static class BatchPreviewDiff
{
    public const int MaxExcerptLinesPerSide = PlcContentDiff.MaxExcerptLinesPerSide;
    public const int MaxExcerptCharsPerSide = PlcContentDiff.MaxExcerptCharsPerSide;
    public const int MaxExcerptCharsPerLine = PlcContentDiff.MaxExcerptCharsPerLine;
    public const int MaxBatchExcerptLines = PlcContentDiff.MaxCallExcerptLines;
    public const int MaxBatchExcerptChars = PlcContentDiff.MaxCallExcerptChars;

    // Adapter over PlcContentDiff; deleted with the batch tools.
    public static BatchPreviewDiffDocument? Build(
        IReadOnlyList<BatchOperationRequest> operations,
        IReadOnlyList<OperationBatchCurrentState> states)
    {
        if (operations.Count != states.Count)
        {
            throw new ArgumentException("Operations and current-state rows must align by index.");
        }

        var budget = new PlcContentDiffBudget();
        var entries = new List<BatchPreviewDiffEntry>();
        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            if (!IsEligible(operation, out var requestedText, out var normalizedFormat))
            {
                continue;
            }

            var e = PlcContentDiff.Build(states[index].CurrentState, requestedText, budget);
            entries.Add(new BatchPreviewDiffEntry(
                operation.OperationId,
                operation.Operation,
                normalizedFormat,
                ToSide(e.Current),
                ToSide(e.Requested),
                e.RawTextEqual,
                e.NormalizedLinesEqual,
                e.LineEndingOnly,
                e.UnchangedPrefixLineCount,
                e.UnchangedSuffixLineCount,
                e.CurrentChangedLineCount,
                e.RequestedChangedLineCount,
                e.BudgetExhausted));
        }

        return entries.Count == 0 ? null : new BatchPreviewDiffDocument(entries);
    }

    private static BatchPreviewDiffSide ToSide(PlcContentDiffSide side) => new(
        side.Sha256,
        side.CharacterCount,
        side.LineCount,
        new BatchPreviewDiffExcerpt(
            side.Excerpt.Lines
                .Select(line => new BatchPreviewDiffLine(line.LineNumber, line.Text, line.OmittedCharacterCount))
                .ToArray(),
            side.Excerpt.OmittedLineCount,
            side.Excerpt.OmittedCharacterCount,
            side.Excerpt.BudgetExhausted));

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
