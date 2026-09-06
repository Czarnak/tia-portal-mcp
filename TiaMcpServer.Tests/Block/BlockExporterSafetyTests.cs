using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public sealed class BlockExporterSafetyTests
{
    [Fact]
    public void SafetyExport_RejectsXmlFailureEvenWhenCompanionExportSucceeds()
    {
        var companionCalls = 0;
        void ExportCompanions(ICollection<BlockImportDocument> documents)
        {
            companionCalls++;
            documents.Add(new BlockImportDocument("Mixer.s7dcl", "Mixer.s7dcl", "PRIVATE_COMPANION"));
        }

        var publicBundle = BlockExporter.ExportXmlBundle("PLC_1/Blocks/Mixer", "Mixer",
            () => throw new InvalidOperationException("PRIVATE_XML_ERROR"), ExportCompanions,
            requireAuthoritativeXml: false);
        Assert.Contains("PRIVATE_COMPANION", publicBundle);
        Assert.Equal(1, companionCalls);

        var failure = Assert.Throws<WorkerOperationException>(() => BlockExporter.ExportXmlBundle(
            "PLC_1/Blocks/Mixer", "Mixer", () => throw new InvalidOperationException("PRIVATE_XML_ERROR"),
            ExportCompanions, requireAuthoritativeXml: true));
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, failure.FailureCategory);
        Assert.DoesNotContain("PRIVATE_XML_ERROR", failure.Message);
        Assert.DoesNotContain("PRIVATE_COMPANION", failure.Message);
        Assert.Equal(1, companionCalls);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not XML")]
    public void SafetyExport_RejectsInvalidAuthoritativeXml(string xml)
    {
        Assert.Throws<WorkerOperationException>(() => BlockExporter.ExportXmlBundle(
            "PLC_1/Blocks/Mixer", "Mixer", () => xml,
            documents => documents.Add(new BlockImportDocument("Mixer.s7dcl", "Mixer.s7dcl", "companion")),
            requireAuthoritativeXml: true));
    }

    [Fact]
    public void SafetyExport_WithAuthoritativeXmlPreservesThePublicBundle()
    {
        string Export(bool strict) => BlockExporter.ExportXmlBundle("PLC_1/Blocks/Mixer", "Mixer",
            () => "<Document><SW.Blocks.FC /></Document>",
            documents => documents.Add(new BlockImportDocument("Mixer.s7dcl", "Mixer.s7dcl", "companion")),
            requireAuthoritativeXml: strict);

        Assert.Equal(Export(false), Export(true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExportXmlBundle_DiscardsPartialCompanionPackageWhenCompanionExportFails(bool strict)
    {
        const string xml = "<Document><SW.Blocks.FC /></Document>";

        var bundle = BlockExporter.ExportXmlBundle(
            "PLC_1/Blocks/Mixer",
            "Mixer",
            () => xml,
            documents =>
            {
                documents.Add(new BlockImportDocument(
                    "Mixer.s7dcl", "Mixer.s7dcl", "PARTIAL_COMPANION"));
                throw new InvalidOperationException("later companion failed");
            },
            requireAuthoritativeXml: strict);

        Assert.Equal(
            BlockBundleFormat.Compose(new[]
            {
                new BlockImportDocument("Mixer.xml", "Mixer.xml", xml)
            }),
            bundle);
        Assert.DoesNotContain("PARTIAL_COMPANION", bundle);
    }
}
