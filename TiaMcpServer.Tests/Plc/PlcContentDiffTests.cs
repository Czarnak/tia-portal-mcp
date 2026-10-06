using System.Security.Cryptography;
using System.Text;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

public sealed class PlcContentDiffTests
{
    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [Fact]
    public void Build_UsesRawHashesAndCounts()
    {
        const string current = "DATA_BLOCK \"Recipe\"\r\nBEGIN\r\nEND_DATA_BLOCK\r\n";
        const string requested = "DATA_BLOCK \"Recipe\"\r\nBEGIN\r\n  Value := 1;\r\nEND_DATA_BLOCK\r\n";

        var diff = PlcContentDiff.Build(current, requested);

        Assert.Equal(Sha256(current), diff.Current.Sha256);
        Assert.Equal(Sha256(requested), diff.Requested.Sha256);
        Assert.Equal(current.Length, diff.Current.CharacterCount);
        Assert.Equal(1, diff.RequestedChangedLineCount - diff.CurrentChangedLineCount);
    }

    [Fact]
    public void Build_IdenticalContent_ProducesEmptyExcerptsAndZeroChangedSpans()
    {
        const string content = "TYPE \"A\"\r\nSTRUCT\r\nEND_STRUCT;\r\nEND_TYPE\r\n";

        var diff = PlcContentDiff.Build(content, content);

        Assert.True(diff.RawTextEqual);
        Assert.True(diff.NormalizedLinesEqual);
        Assert.False(diff.LineEndingOnly);
        Assert.Equal(0, diff.CurrentChangedLineCount);
        Assert.Equal(0, diff.RequestedChangedLineCount);
        Assert.Empty(diff.Current.Excerpt.Lines);
        Assert.Empty(diff.Requested.Excerpt.Lines);
    }

    [Fact]
    public void Build_DetectsLineEndingOnlyDifference()
    {
        var diff = PlcContentDiff.Build("TYPE \"A\"\r\nEND_TYPE\r\n", "TYPE \"A\"\nEND_TYPE\n");

        Assert.False(diff.RawTextEqual);
        Assert.True(diff.NormalizedLinesEqual);
        Assert.True(diff.LineEndingOnly);
    }

    [Fact]
    public void Build_TruncatesChangedSpanToFirstAndLastTwentyLinesPerSide()
    {
        var current = string.Join("\r\n", Enumerable.Range(1, 60).Select(i => $"OLD {i}")) + "\r\n";
        var requested = string.Join("\r\n", Enumerable.Range(1, 60).Select(i => $"NEW {i}")) + "\r\n";

        var diff = PlcContentDiff.Build(current, requested);

        Assert.Equal(40, diff.Current.Excerpt.Lines.Count);
        Assert.Equal(40, diff.Requested.Excerpt.Lines.Count);
        Assert.Equal("OLD 1", diff.Current.Excerpt.Lines[0].Text);
        Assert.Equal("OLD 60", diff.Current.Excerpt.Lines[^1].Text);
        Assert.Equal("NEW 1", diff.Requested.Excerpt.Lines[0].Text);
        Assert.Equal("NEW 60", diff.Requested.Excerpt.Lines[^1].Text);
        Assert.Equal(20, diff.Current.Excerpt.OmittedLineCount);
        Assert.Equal(20, diff.Requested.Excerpt.OmittedLineCount);
    }

    [Fact]
    public void Build_TruncatesLongLinesAndReportsOmittedCharacters()
    {
        var longLine = new string('x', 549);

        var diff = PlcContentDiff.Build("TYPE \"A\"\r\nEND_TYPE\r\n", $"TYPE \"A\"\r\n{longLine}\r\nEND_TYPE\r\n");
        var line = diff.Requested.Excerpt.Lines.Single(x => x.LineNumber == 2);

        Assert.Equal(512, line.Text.Length);
        Assert.Equal(37, line.OmittedCharacterCount);
        Assert.Equal(37, diff.Requested.Excerpt.OmittedCharacterCount);
    }

    [Fact]
    public void Build_UsesLiteral8192CharacterPerSideLimit()
    {
        var current = string.Join("\r\n", Enumerable.Range(1, 32).Select(i => $"OLD {i}"));
        var requested = string.Join("\r\n", Enumerable.Repeat(new string('x', 512), 32));

        var excerpt = PlcContentDiff.Build(current, requested).Requested.Excerpt;

        Assert.Equal(32, excerpt.Lines.Count);
        Assert.All(excerpt.Lines, line => Assert.Equal(256, line.Text.Length));
        Assert.Equal(8_192, excerpt.Lines.Sum(line => line.Text.Length));
        Assert.Equal(8_192, excerpt.OmittedCharacterCount);
    }

    [Fact]
    public void Build_ExhaustsAtLiteral32768CharacterCallLimit()
    {
        var current = string.Join("\r\n", Enumerable.Repeat(new string('a', 600), 16));
        var requested = string.Join("\r\n", Enumerable.Repeat(new string('b', 600), 16));
        var budget = new PlcContentDiffBudget();

        var diffs = Enumerable.Range(0, 3).Select(_ => PlcContentDiff.Build(current, requested, budget)).ToArray();

        Assert.All(diffs.Take(2), diff => Assert.False(diff.BudgetExhausted));
        Assert.Equal(32_768, diffs.Take(2).Sum(diff =>
            diff.Current.Excerpt.Lines.Sum(line => line.Text.Length)
            + diff.Requested.Excerpt.Lines.Sum(line => line.Text.Length)));
        Assert.True(diffs[2].BudgetExhausted);
        Assert.Empty(diffs[2].Current.Excerpt.Lines);
        Assert.Empty(diffs[2].Requested.Excerpt.Lines);
    }

    [Fact]
    public void Build_ExhaustsTheCallLineBudgetAtTheFifthDiff_AndLaterEntriesKeepHashesAndCounts()
    {
        var current = string.Join("\r\n", Enumerable.Range(1, 60).Select(i => $"OLD {i}")) + "\r\n";
        var requested = string.Join("\r\n", Enumerable.Range(1, 60).Select(i => $"NEW {i}")) + "\r\n";
        var budget = new PlcContentDiffBudget();

        var diffs = Enumerable.Range(0, 6).Select(_ => PlcContentDiff.Build(current, requested, budget)).ToArray();

        Assert.All(diffs.Take(4), diff => Assert.NotEmpty(diff.Requested.Excerpt.Lines));
        Assert.True(diffs[4].BudgetExhausted);
        Assert.Empty(diffs[4].Current.Excerpt.Lines);
        var exhausted = diffs[5];
        Assert.True(exhausted.BudgetExhausted);
        Assert.Empty(exhausted.Requested.Excerpt.Lines);
        Assert.Equal(Sha256(current), exhausted.Current.Sha256);
        Assert.Equal(current.Length, exhausted.Current.CharacterCount);
        Assert.Equal(61, exhausted.Current.LineCount);
        Assert.Equal(Sha256(requested), exhausted.Requested.Sha256);
        Assert.Equal(61, exhausted.Requested.LineCount);
        Assert.False(exhausted.RawTextEqual);
        Assert.False(exhausted.NormalizedLinesEqual);
        Assert.False(exhausted.LineEndingOnly);
    }
}
