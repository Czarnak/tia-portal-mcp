using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

public class PlcContentDiffSidesTests
{
    private const string Xml = "<Document>\r\n  <Network>A</Network>\r\n</Document>\r\n";
    private const string EditedXml = "<Document>\r\n  <Network>B</Network>\r\n</Document>\r\n";
    private const string Companion = "BLOCK B\r\n";

    private static string CurrentExport => BlockBundleText.Compose(new[]
    {
        new BlockImportDocument("B.xml", "B.xml", Xml),
        new BlockImportDocument("B.s7dcl", "B.s7dcl", Companion),
    });

    [Fact]
    public void RawXmlSubmissionDiffsAgainstXmlPart()
    {
        var sides = PlcContentDiffSides.Select("xml", CurrentExport, EditedXml);

        Assert.Equal(Xml, sides.Current);
        Assert.Equal(EditedXml, sides.Requested);
    }

    [Fact]
    public void OneDocumentBundleDiffsWithoutSeparator()
    {
        var sides = PlcContentDiffSides.Select("xml", CurrentExport, "--- FILE: B.xml ---\n" + EditedXml);

        Assert.Equal(Xml, sides.Current);
        Assert.Equal(EditedXml, sides.Requested);
    }

    [Fact]
    public void XmlOnlyLfSubmissionIsLineEndingOnly()
    {
        var sides = PlcContentDiffSides.Select("xml", CurrentExport, Xml.Replace("\r\n", "\n"));

        Assert.True(PlcContentDiff.Build(sides.Current, sides.Requested).LineEndingOnly);
    }

    [Fact]
    public void FullBundleSubmissionKeepsWholeBundleDiff()
    {
        var submitted = BlockBundleText.Compose(new[]
        {
            new BlockImportDocument("B.xml", "B.xml", Xml),
            new BlockImportDocument("B.s7dcl", "B.s7dcl", "edited"),
        });

        var sides = PlcContentDiffSides.Select("xml", CurrentExport, submitted);

        Assert.Equal(CurrentExport, sides.Current);
        Assert.Equal(submitted, sides.Requested);
    }

    [Fact]
    public void UnparsableSidesFallBackToWholeText()
    {
        var badSubmission = PlcContentDiffSides.Select("xml", CurrentExport, "--- FILE: bad\n<x/>");
        var badExport = PlcContentDiffSides.Select("xml", "--- FILE: bad\n", EditedXml);

        Assert.Equal((CurrentExport, "--- FILE: bad\n<x/>"), badSubmission);
        Assert.Equal(("--- FILE: bad\n", EditedXml), badExport);
    }

    [Fact]
    public void CompanionOnlyExportFallsBackToWholeText()
    {
        var export = BlockBundleText.Compose(new[] { new BlockImportDocument("B.s7dcl", "B.s7dcl", Companion) });

        var sides = PlcContentDiffSides.Select("xml", export, Xml);

        Assert.Equal((export, Xml), sides);
    }

    [Fact]
    public void SourceFormatIsUntouched()
    {
        var sides = PlcContentDiffSides.Select("source", CurrentExport, EditedXml);

        Assert.Equal((CurrentExport, EditedXml), sides);
    }
}
