using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class BatchPreviewDiffTests
{
    private static BatchOperationRequest BlockOp(string id, string path, string requested, string? format = null)
        => new()
        {
            OperationId = id,
            Operation = "update_block_logic",
            BlockPath = path,
            YamlContent = requested,
            Format = format,
        };

    private static BatchOperationRequest TypeOp(string id, string path, string requested, string? format = null)
        => new()
        {
            OperationId = id,
            Operation = "update_type_content",
            TypePath = path,
            SourceContent = requested,
            Format = format,
        };

    private static OperationBatchCurrentState State(BatchOperationRequest op, string current)
        => new(op.OperationId, op.Operation, current);

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [Fact]
    public void Build_ReturnsNullWhenBatchHasNoEligibleTextReplacement()
    {
        var operations = new[]
        {
            new BatchOperationRequest
            {
                OperationId = "tag-1",
                Operation = "create_tag_table",
                TableName = "Inputs",
            }
        };

        var states = new[]
        {
            new OperationBatchCurrentState("tag-1", "create_tag_table", "{\"table\":\"Inputs\"}")
        };

        Assert.Null(BatchPreviewDiff.Build(operations, states));
    }

    [Fact]
    public void Build_UsesNormalizedWriteFormatAndRawHashesForEligibleOperations()
    {
        const string current = "DATA_BLOCK \"Recipe\"\r\nBEGIN\r\nEND_DATA_BLOCK\r\n";
        const string requested = "DATA_BLOCK \"Recipe\"\r\nBEGIN\r\n  Value := 1;\r\nEND_DATA_BLOCK\r\n";
        var op = BlockOp("block-1", "PLC_1/Blocks/Recipe_DB", requested, SourceFormatNames.Source);

        var diff = BatchPreviewDiff.Build(new[] { op }, new[] { State(op, current) })!;
        var entry = Assert.Single(diff.Operations);

        Assert.Equal(SourceFormatNames.Source, entry.Format);
        Assert.Equal(Sha256(current), entry.Current.Sha256);
        Assert.Equal(Sha256(requested), entry.Requested.Sha256);
    }

    [Fact]
    public void Build_IdenticalContent_ProducesEmptyExcerptsAndZeroChangedSpans()
    {
        const string content = "TYPE \"A\"\r\nSTRUCT\r\nEND_STRUCT;\r\nEND_TYPE\r\n";
        var op = TypeOp("type-1", "PLC_1/Types/A", content);

        var diff = BatchPreviewDiff.Build(new[] { op }, new[] { State(op, content) })!;
        var entry = Assert.Single(diff.Operations);

        Assert.True(entry.RawTextEqual);
        Assert.True(entry.NormalizedLinesEqual);
        Assert.False(entry.LineEndingOnly);
        Assert.Equal(0, entry.CurrentChangedLineCount);
        Assert.Equal(0, entry.RequestedChangedLineCount);
        Assert.Empty(entry.Current.Excerpt.Lines);
        Assert.Empty(entry.Requested.Excerpt.Lines);
    }

    [Fact]
    public void Build_DetectsLineEndingOnlyDifference()
    {
        const string current = "TYPE \"A\"\r\nSTRUCT\r\nEND_STRUCT;\r\nEND_TYPE\r\n";
        const string requested = "TYPE \"A\"\nSTRUCT\nEND_STRUCT;\nEND_TYPE\n";
        var op = TypeOp("type-1", "PLC_1/Types/A", requested);

        var diff = BatchPreviewDiff.Build(new[] { op }, new[] { State(op, current) })!;
        var entry = Assert.Single(diff.Operations);

        Assert.False(entry.RawTextEqual);
        Assert.True(entry.NormalizedLinesEqual);
        Assert.True(entry.LineEndingOnly);
    }

    [Fact]
    public void Build_TruncatesChangedSpanToFirstAndLastTwentyLinesPerSide()
    {
        var current = string.Join("\r\n", Enumerable.Range(1, 60).Select(i => $"OLD {i}")) + "\r\n";
        var requested = string.Join("\r\n", Enumerable.Range(1, 60).Select(i => $"NEW {i}")) + "\r\n";
        var op = TypeOp("type-1", "PLC_1/Types/A", requested);

        var diff = BatchPreviewDiff.Build(new[] { op }, new[] { State(op, current) })!;
        var entry = Assert.Single(diff.Operations);

        Assert.Equal(40, entry.Current.Excerpt.Lines.Count);
        Assert.Equal(40, entry.Requested.Excerpt.Lines.Count);
        Assert.Equal("OLD 1", entry.Current.Excerpt.Lines[0].Text);
        Assert.Equal("OLD 60", entry.Current.Excerpt.Lines[^1].Text);
        Assert.Equal("NEW 1", entry.Requested.Excerpt.Lines[0].Text);
        Assert.Equal("NEW 60", entry.Requested.Excerpt.Lines[^1].Text);
        Assert.Equal(20, entry.Current.Excerpt.OmittedLineCount);
        Assert.Equal(20, entry.Requested.Excerpt.OmittedLineCount);
    }

    [Fact]
    public void Build_TruncatesLongLinesAndReportsOmittedCharacters()
    {
        var longLine = new string('x', 549);
        var current = "TYPE \"A\"\r\nEND_TYPE\r\n";
        var requested = $"TYPE \"A\"\r\n{longLine}\r\nEND_TYPE\r\n";
        var op = TypeOp("type-1", "PLC_1/Types/A", requested);

        var diff = BatchPreviewDiff.Build(new[] { op }, new[] { State(op, current) })!;
        var line = diff.Operations[0].Requested.Excerpt.Lines.Single(x => x.LineNumber == 2);

        Assert.Equal(512, line.Text.Length);
        Assert.Equal(37, line.OmittedCharacterCount);
        Assert.Equal(37, diff.Operations[0].Requested.Excerpt.OmittedCharacterCount);
    }

    [Fact]
    public void Build_UsesLiteral8192CharacterPerSideLimit()
    {
        var current = string.Join("\r\n", Enumerable.Range(1, 16).Select(i => $"OLD {i}"));
        var requested = string.Join("\r\n", Enumerable.Repeat(new string('x', 600), 16));
        var op = TypeOp("type-1", "PLC_1/Types/A", requested);

        var diff = BatchPreviewDiff.Build(new[] { op }, new[] { State(op, current) })!;
        var excerpt = Assert.Single(diff.Operations).Requested.Excerpt;

        Assert.Equal(16, excerpt.Lines.Count);
        Assert.All(excerpt.Lines, line => Assert.Equal(512, line.Text.Length));
        Assert.Equal(8_192, excerpt.Lines.Sum(line => line.Text.Length));
        Assert.Equal(1_408, excerpt.OmittedCharacterCount);
    }

    [Fact]
    public void Build_ExhaustsAtLiteral32768CharacterBatchLimit()
    {
        var current = string.Join("\r\n", Enumerable.Repeat(new string('a', 600), 16));
        var requested = string.Join("\r\n", Enumerable.Repeat(new string('b', 600), 16));
        var operations = Enumerable.Range(1, 3)
            .Select(i => TypeOp($"type-{i}", $"PLC_1/Types/T{i}", requested))
            .ToArray();
        var states = operations.Select(op => State(op, current)).ToArray();

        var diff = BatchPreviewDiff.Build(operations, states)!;

        Assert.All(diff.Operations.Take(2), entry => Assert.False(entry.BatchBudgetExhausted));
        Assert.Equal(32_768, diff.Operations.Take(2).Sum(entry =>
            entry.Current.Excerpt.Lines.Sum(line => line.Text.Length)
            + entry.Requested.Excerpt.Lines.Sum(line => line.Text.Length)));
        Assert.True(diff.Operations[2].BatchBudgetExhausted);
        Assert.Empty(diff.Operations[2].Current.Excerpt.Lines);
        Assert.Empty(diff.Operations[2].Requested.Excerpt.Lines);
    }

    [Fact]
    public void Build_ExhaustsTheWholeBatchLineBudgetAtTheFifthEligibleOperation_AndLaterEntriesKeepHashesAndCounts()
    {
        var current = string.Join("\r\n", Enumerable.Range(1, 60).Select(i => $"OLD {i}")) + "\r\n";
        var requested = string.Join("\r\n", Enumerable.Range(1, 60).Select(i => $"NEW {i}")) + "\r\n";
        var operations = Enumerable.Range(1, 6)
            .Select(i => TypeOp($"type-{i}", $"PLC_1/Types/T{i}", requested))
            .ToArray();
        var states = operations.Select(op => State(op, current)).ToArray();

        var diff = BatchPreviewDiff.Build(operations, states)!;

        Assert.All(diff.Operations.Take(4), entry => Assert.NotEmpty(entry.Requested.Excerpt.Lines));
        Assert.True(diff.Operations[4].BatchBudgetExhausted);
        Assert.Empty(diff.Operations[4].Current.Excerpt.Lines);
        Assert.Empty(diff.Operations[4].Requested.Excerpt.Lines);
        Assert.True(diff.Operations[5].BatchBudgetExhausted);
        Assert.Empty(diff.Operations[5].Current.Excerpt.Lines);
        Assert.Empty(diff.Operations[5].Requested.Excerpt.Lines);
        var exhausted = diff.Operations[5];
        Assert.Equal(Sha256(current), exhausted.Current.Sha256);
        Assert.Equal(current.Length, exhausted.Current.CharacterCount);
        Assert.Equal(61, exhausted.Current.LineCount);
        Assert.Equal(Sha256(requested), exhausted.Requested.Sha256);
        Assert.Equal(requested.Length, exhausted.Requested.CharacterCount);
        Assert.Equal(61, exhausted.Requested.LineCount);
        Assert.False(exhausted.RawTextEqual);
        Assert.False(exhausted.NormalizedLinesEqual);
        Assert.False(exhausted.LineEndingOnly);
    }
}
