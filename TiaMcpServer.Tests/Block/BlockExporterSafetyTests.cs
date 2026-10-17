using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public sealed class BlockExporterSafetyTests
{
    [Fact]
    public void ExportXmlBundle_FallsBackToCompanionsWhenXmlExportFails()
    {
        var companionCalls = 0;
        void ExportCompanions(ICollection<BlockImportDocument> documents)
        {
            companionCalls++;
            documents.Add(new BlockImportDocument("Mixer.s7dcl", "Mixer.s7dcl", "PRIVATE_COMPANION"));
        }

        var publicBundle = BlockExporter.ExportXmlBundle("PLC_1/Blocks/Mixer", "Mixer",
            () => throw new InvalidOperationException("PRIVATE_XML_ERROR"), ExportCompanions);
        Assert.Contains("PRIVATE_COMPANION", publicBundle);
        Assert.DoesNotContain("PRIVATE_XML_ERROR", publicBundle);
        Assert.Equal(1, companionCalls);
    }

    [Fact]
    public void ExportXmlBundle_DiscardsPartialCompanionPackageWhenCompanionExportFails()
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
            });

        Assert.Equal(
            BlockBundleFormat.Compose(new[]
            {
                new BlockImportDocument("Mixer.xml", "Mixer.xml", xml)
            }),
            bundle);
        Assert.DoesNotContain("PARTIAL_COMPANION", bundle);
    }
}
