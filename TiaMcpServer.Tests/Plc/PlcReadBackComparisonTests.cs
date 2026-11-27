using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

public class PlcReadBackComparisonTests
{
    private const string Xml = "<Document>\r\n  <Network>A</Network>\r\n</Document>\r\n";
    private const string Companion = "BLOCK B\r\n";

    private static string ObservedBundle => BlockBundleText.Compose(new[]
    {
        new BlockImportDocument("B.xml", "B.xml", Xml),
        new BlockImportDocument("B.s7dcl", "B.s7dcl", Companion),
    });

    [Fact]
    public void NormalizedConstantReportsSubmittedAndObservedText()
    {
        const string submitted = "<ConstantType>LReal</ConstantType>";
        const string observed = "<ConstantType>Real</ConstantType>";
        var result = PlcReadBackComparison.Build("xml", submitted, observed, new());
        Assert.Equal("xmlDocument", result.ComparisonScope);
        Assert.False(result.RawTextEqual);
        Assert.False(result.NormalizedLinesEqual);
        Assert.False(result.LineEndingOnly);
        Assert.Equal(submitted, Assert.Single(result.Submitted.Excerpt.Lines).Text);
        Assert.Equal(observed, Assert.Single(result.Observed.Excerpt.Lines).Text);
        Assert.Null(result.Omission);
    }

    [Fact]
    public void NewlineOnlyDifferenceKeepsTheExactTextDistinction()
    {
        var result = PlcReadBackComparison.Build("source", "A\r\nB\r", "A\nB\n", new());
        Assert.Equal("wholeText", result.ComparisonScope);
        Assert.False(result.RawTextEqual);
        Assert.True(result.NormalizedLinesEqual);
        Assert.True(result.LineEndingOnly);
        Assert.Empty(result.Submitted.Excerpt.Lines);
        Assert.Empty(result.Observed.Excerpt.Lines);
    }

    [Fact]
    public void GenerationExhaustionPreservesComparisonSummary()
    {
        var result = PlcReadBackComparison.Build("source", "old", "new",
            new PlcContentDiffBudget { RemainingLines = 0, RemainingChars = 0 });
        Assert.False(result.RawTextEqual);
        Assert.True(result.BudgetExhausted);
        Assert.Equal(1, result.SubmittedChangedSpanLineCount);
        Assert.Equal(1, result.ObservedChangedSpanLineCount);
        Assert.Empty(result.Submitted.Excerpt.Lines);
        Assert.True(result.Submitted.Excerpt.BudgetExhausted);
        Assert.Null(result.Omission);
    }

    [Theory]
    [InlineData("xml", "xmlDocument")]
    [InlineData("source", "wholeText")]
    public void IdenticalSelectedTextHasEmptyExcerpts(string format, string scope)
    {
        var result = PlcReadBackComparison.Build(format, Xml, Xml, new());
        Assert.Equal(scope, result.ComparisonScope);
        Assert.True(result.RawTextEqual);
        Assert.True(result.NormalizedLinesEqual);
        Assert.False(result.LineEndingOnly);
        Assert.Empty(result.Submitted.Excerpt.Lines);
        Assert.Empty(result.Observed.Excerpt.Lines);
        Assert.Equal(0, result.SubmittedChangedSpanLineCount);
        Assert.Equal(0, result.ObservedChangedSpanLineCount);
        Assert.Null(result.Omission);
    }

    [Fact]
    public void XmlOnlySubmissionIgnoresObservedCompanions()
    {
        Assert.NotEqual(Xml, ObservedBundle);
        var result = PlcReadBackComparison.Build("xml", Xml, ObservedBundle, new());
        Assert.Equal("xmlDocument", result.ComparisonScope);
        Assert.True(result.RawTextEqual);
        Assert.True(result.NormalizedLinesEqual);
        Assert.Equal(Xml.Length, result.Submitted.CharacterCount);
        Assert.Equal(Xml.Length, result.Observed.CharacterCount);
        Assert.Equal(result.Submitted.Sha256, result.Observed.Sha256);
        Assert.Empty(result.Submitted.Excerpt.Lines);
        Assert.Empty(result.Observed.Excerpt.Lines);
    }

    [Fact]
    public void SingleXmlBundleUsesXmlDocumentScope()
    {
        var submitted = "--- FILE: B.xml ---\n" + Xml;
        var result = PlcReadBackComparison.Build("xml", submitted, ObservedBundle, new());
        Assert.Equal("xmlDocument", result.ComparisonScope);
        Assert.True(result.RawTextEqual);
        Assert.True(result.NormalizedLinesEqual);
        Assert.Equal(Xml.Length, result.Submitted.CharacterCount);
        Assert.Equal(Xml.Length, result.Observed.CharacterCount);
    }

    [Fact]
    public void MultipleDocumentSubmissionUsesWholeTextScope()
    {
        var submitted = BlockBundleText.Compose(new[]
        {
            new BlockImportDocument("B.xml", "B.xml", Xml),
            new BlockImportDocument("B.s7dcl", "B.s7dcl", "edited"),
        });
        var result = PlcReadBackComparison.Build("xml", submitted, ObservedBundle, new());
        Assert.Equal("wholeText", result.ComparisonScope);
        Assert.False(result.RawTextEqual);
        Assert.False(result.NormalizedLinesEqual);
        Assert.Equal(submitted.Length, result.Submitted.CharacterCount);
        Assert.Equal(ObservedBundle.Length, result.Observed.CharacterCount);
    }

    [Fact]
    public void CompanionOnlyObservedExportUsesWholeTextFallback()
    {
        var observed = BlockBundleText.Compose(new[]
        {
            new BlockImportDocument("B.s7dcl", "B.s7dcl", Companion),
        });
        var result = PlcReadBackComparison.Build("xml", Xml, observed, new());
        Assert.Equal("wholeText", result.ComparisonScope);
        Assert.Equal(Xml.Length, result.Submitted.CharacterCount);
        Assert.Equal(observed.Length, result.Observed.CharacterCount);
        Assert.False(result.RawTextEqual);
    }

    [Theory]
    [InlineData("--- FILE: bad\n<x/>", "<x/>")]
    [InlineData("<x/>", "--- FILE: bad\n")]
    public void MalformedBundleUsesWholeTextFallback(string submitted, string observed)
    {
        var result = PlcReadBackComparison.Build("xml", submitted, observed, new());
        Assert.Equal("wholeText", result.ComparisonScope);
        Assert.Equal(submitted.Length, result.Submitted.CharacterCount);
        Assert.Equal(observed.Length, result.Observed.CharacterCount);
        Assert.False(result.RawTextEqual);
    }

    [Fact]
    public void DisjointDifferencesCountTheWholeChangedSpan()
    {
        var result = PlcReadBackComparison.Build("source", "a\nkeep\nb", "x\nkeep\ny", new());
        Assert.Equal(3, result.SubmittedChangedSpanLineCount);
        Assert.Equal(3, result.ObservedChangedSpanLineCount);
        Assert.Equal(new[] { "a", "keep", "b" }, result.Submitted.Excerpt.Lines.Select(line => line.Text));
        Assert.Equal(new[] { "x", "keep", "y" }, result.Observed.Excerpt.Lines.Select(line => line.Text));
    }

    [Fact]
    public void SubmittedAndObservedCountsFollowTheirOwnText()
    {
        var result = PlcReadBackComparison.Build("source", "a\nz", "b", new());
        Assert.Equal(2, result.SubmittedChangedSpanLineCount);
        Assert.Equal(1, result.ObservedChangedSpanLineCount);
        Assert.Equal(3, result.Submitted.CharacterCount);
        Assert.Equal(1, result.Observed.CharacterCount);
        Assert.Equal(2, result.Submitted.LineCount);
        Assert.Equal(1, result.Observed.LineCount);
        Assert.Equal(new[] { "a", "z" }, result.Submitted.Excerpt.Lines.Select(line => line.Text));
        Assert.Equal("b", Assert.Single(result.Observed.Excerpt.Lines).Text);
        Assert.NotEqual(result.Submitted.Sha256, result.Observed.Sha256);
    }

    [Fact]
    public void GenerationExhaustionOnEqualTextOmitsNothing()
    {
        var result = PlcReadBackComparison.Build("source", "same", "same",
            new PlcContentDiffBudget { RemainingLines = 0, RemainingChars = 0, Exhausted = true });
        Assert.True(result.RawTextEqual);
        Assert.True(result.NormalizedLinesEqual);
        Assert.True(result.BudgetExhausted);
        foreach (var side in new[] { result.Submitted, result.Observed })
        {
            Assert.Empty(side.Excerpt.Lines);
            Assert.Equal(0, side.Excerpt.OmittedLineCount);
            Assert.Equal(0, side.Excerpt.OmittedCharacterCount);
        }
        Assert.Null(result.Omission);
    }
}
