using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

public class PlcBlockLogicPatchTests
{
    [Theory]
    [InlineData("abc", "b", "B", "aBc")]
    [InlineData("abc", "b", "", "ac")]
    [InlineData("abc", "abc", "", "")]
    [InlineData("A\r\nB\r\nC", "A\nB", "X\nY", "X\r\nY\r\nC")]
    [InlineData("A\nB\nC", "A\r\nB", "X\r\nY", "X\nY\nC")]
    [InlineData("A\r\nB\r\n", "B", "X\nY", "A\r\nX\r\nY\r\n")]
    [InlineData("A\r\nB\nC", "A\nB\nC", "X\nY", "X\r\nY")]
    [InlineData("A\nB", "B", "X\nY", "A\nX\r\nY")]
    [InlineData("A\rB", "A\rB", "X\rY", "X\rY")]
    [InlineData("before\nA\nB\nafter", "A\nB", "X\r\nY", "before\nX\nY\nafter")]
    public void ResolvesExactOriginalSpansAndLineEndings(string original, string oldText, string newText, string expected)
    {
        Assert.Equal(expected, Apply(original, Edit(oldText, newText)));
    }

    [Theory]
    [InlineData("abc", " \r\n\t", "nonblank")]
    [InlineData("abc", "z", "matched zero times")]
    [InlineData("aaaa", "aaa", "matched several times")]
    [InlineData("A\rB", "A\nB", "matched zero times")]
    public void RefusesInvalidAnchorsWithoutEchoingOriginal(string original, string oldText, string expectedMessage)
    {
        var exception = Assert.Throws<ArgumentException>(() => Apply(original, Edit(oldText, "X")));

        Assert.Contains("edits[0]", exception.Message);
        Assert.Contains(expectedMessage, exception.Message);
    }

    [Fact]
    public void RefusesIntersectingEditsWithBothIndices()
    {
        var exception = Assert.Throws<ArgumentException>(() => Apply("abcdef", Edit("bcd", "X"), Edit("def", "Y")));

        Assert.Contains("edits[0]", exception.Message);
        Assert.Contains("edits[1]", exception.Message);
        Assert.Contains("overlap", exception.Message);
    }

    [Fact]
    public void AllowsAdjacentSpans()
    {
        Assert.Equal("aXYf", Apply("abcdef", Edit("bc", "X"), Edit("de", "Y")));
    }

    [Fact]
    public void ResolvesOutOfOrderEditsAgainstTheOriginal()
    {
        Assert.Equal("LONGcdZ", Apply("abcdef", Edit("ef", "Z"), Edit("ab", "LONG")));
    }

    [Fact]
    public void DoesNotCascadeAnchorsThroughEarlierReplacements()
    {
        var exception = Assert.Throws<ArgumentException>(() => Apply("cat dog", Edit("cat", "wolf"), Edit("wolf", "fox")));

        Assert.Contains("edits[1]", exception.Message);
        Assert.Contains("matched zero times", exception.Message);
    }

    [Fact]
    public void RefusesRepeatedEditsOfTheSameSpan()
    {
        Assert.Contains("overlap", Assert.Throws<ArgumentException>(() => Apply("abc", Edit("b", "B"), Edit("b", "B"))).Message);
    }

    [Theory]
    [InlineData("B.xml")]
    [InlineData("B.XML")]
    public void XmlScopeIgnoresTheCompanionAndReturnsOnlyXml(string xmlName)
    {
        var bundle = Bundle((xmlName, "<Document>needle</Document>\n"), ("B.s7dcl", "needle\n"));

        Assert.Equal("<Document>changed</Document>\n", PlcBlockLogicPatch.Apply("xml", bundle, [Edit("needle", "changed")]));
    }

    [Fact]
    public void XmlScopeDoesNotFindACompanionOnlyAnchor()
    {
        var bundle = Bundle(("B.xml", "<Document/>\n"), ("B.s7dcl", "needle\n"));

        Assert.Contains("matched zero times", Assert.Throws<ArgumentException>(() => PlcBlockLogicPatch.Apply("xml", bundle, [Edit("needle", "changed")])).Message);
    }

    [Fact]
    public void RawXmlIsTheAuthoritativeDocument()
    {
        Assert.Equal("<Document>B</Document>", PlcBlockLogicPatch.Apply("xml", "<Document>A</Document>", [Edit(">A<", ">B<")]));
    }

    [Fact]
    public void MissingXmlExplainsCompileAndReadRecovery()
    {
        var bundle = Bundle(("B.s7dcl", "source"));
        var exception = Assert.Throws<ArgumentException>(() => PlcBlockLogicPatch.Apply("xml", bundle, [Edit("source", "changed")]));

        Assert.Contains("This bundle contains no Simatic ML (.xml) document.", exception.Message);
        Assert.Contains("compile it in TIA Portal and read it again before writing.", exception.Message);
    }

    [Fact]
    public void MalformedXmlBundleIsAValidationException()
    {
        var exception = Assert.Throws<ArgumentException>(() => PlcBlockLogicPatch.Apply("xml", "--- FILE: bad\nsource", [Edit("source", "changed")]));

        Assert.Contains("delimiter", exception.Message);
    }

    [Fact]
    public void XmlScopePreservesSeparatorInducedLfAndUsesItsFinalLineEnding()
    {
        var bundle = Bundle(("B.xml", "A\r\nZ"), ("B.s7dcl", "source"));

        Assert.Equal("A\r\nX\nY\n", PlcBlockLogicPatch.Apply("xml", bundle, [Edit("Z", "X\nY")]));
    }

    [Theory]
    [InlineData("\n--- FILE: B.xml ---\n")]
    [InlineData("\n--- FILE: bad")]
    public void RefusesDelimiterCandidatesInReplacement(string replacement)
    {
        var exception = Assert.Throws<ArgumentException>(() => Apply("base", Edit("base", replacement)));

        Assert.Contains("edits[0]", exception.Message);
        Assert.Contains("delimiter", exception.Message);
    }

    [Theory]
    [InlineData("--- X", "X", "FILE: B.xml ---")]
    [InlineData("q--- FILE: B.xml ---", "q", "")]
    [InlineData("head x--- FILE: bad", "head x", "")]
    [InlineData("x--- FILE: bad", "x", "\n")]
    public void RefusesDelimiterCandidatesCreatedAcrossBoundaries(string original, string oldText, string newText)
    {
        Assert.Contains("delimiter", Assert.Throws<ArgumentException>(() => Apply(original, Edit(oldText, newText))).Message);
    }

    [Theory]
    [InlineData("value", "changed", "--- FILE: existing\nchanged")]
    [InlineData("existing", "changed", "--- FILE: changed\nvalue")]
    public void RetainsAnInheritedSourceDelimiterPrefix(string oldText, string newText, string expected)
    {
        Assert.Equal(expected, Apply("--- FILE: existing\nvalue", Edit(oldText, newText)));
    }

    [Fact]
    public void EditingAnInheritedDelimiterPrefixDoesNotCreateAnExemption()
    {
        Assert.Contains("delimiter", Assert.Throws<ArgumentException>(() => Apply("--- FILE: existing\nvalue", Edit("FILE", "FILE"))).Message);
    }

    [Fact]
    public void IntroducingALineStartBeforeAnInheritedMidLinePrefixIsRefused()
    {
        Assert.Contains("delimiter", Assert.Throws<ArgumentException>(() => Apply("prefix--- FILE: existing", Edit("prefix", "\n"))).Message);
    }

    [Fact]
    public void RetainsMultipleInheritedPrefixesAcrossUnchangedSpansAfterEarlierEdits()
    {
        var original = "header\n--- FILE: first\nmiddle\n--- FILE: second\nvalue";

        Assert.Equal("HEADER\n--- FILE: FIRST\nmiddle\n--- FILE: SECOND\nchanged",
            Apply(original, Edit("header", "HEADER"), Edit("first", "FIRST"), Edit("second", "SECOND"), Edit("value", "changed")));
    }

    [Fact]
    public void RefusesDelimiterPrefixesAssembledFromAdjacentReplacements()
    {
        Assert.Contains("delimiter", Assert.Throws<ArgumentException>(() => Apply("aaaBBB", Edit("aaa", "--- "), Edit("BBB", "FILE:"))).Message);
    }

    [Fact]
    public void LargeDocumentChangesOnlyTheRequestedSpan()
    {
        var original = new string('a', 36000) + "unique-anchor" + new string('z', 36128);
        Assert.Equal(72141, original.Length);

        Assert.Equal(new string('a', 36000) + "changed" + new string('z', 36128), Apply(original, Edit("unique-anchor", "changed")));
    }

    private static string Apply(string current, params PlcTextEdit[] edits) => PlcBlockLogicPatch.Apply("source", current, edits);

    private static PlcTextEdit Edit(string oldText, string newText) => new() { OldText = oldText, NewText = newText };

    private static string Bundle(params (string Name, string Content)[] documents) => BlockBundleText.Compose(
        documents.Select(document => new BlockImportDocument(document.Name, document.Name, document.Content)).ToArray());
}
