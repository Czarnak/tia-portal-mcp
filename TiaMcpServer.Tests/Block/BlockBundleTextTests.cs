using TiaMcpServer.Contracts.Block;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class BlockBundleTextTests
{
    [Fact]
    public void ParseThrowsBundleFormatExceptionWithCurrentMessage()
    {
        var exception = Assert.Throws<BlockBundleFormatException>(
            () => BlockBundleText.Parse("a.xml", "--- FILE: a.xml ---\n"));

        Assert.Equal("Every declared block import document must have content.", exception.Message);
    }

    [Fact]
    public void ComposeRoundTripsThroughParse()
    {
        var composed = BlockBundleText.Compose(new[]
        {
            new BlockImportDocument("a.xml", "a.xml", "<x/>\r\n"),
            new BlockImportDocument("a.s7dcl", "a.s7dcl", "d"),
        });

        var parsed = BlockBundleText.Parse("a.xml", composed);

        Assert.Equal(new[] { "a.xml", "a.s7dcl" }, parsed.Documents.Select(d => d.LogicalName));
        Assert.Equal(new[] { "<x/>\r\n", "d" }, parsed.Documents.Select(d => d.Content));
    }

    [Fact]
    public void FindSimaticMlDocumentIgnoresCase()
    {
        var withXml = BlockBundleText.Parse("B.XML", "--- FILE: B.XML ---\n<x/>\n--- FILE: B.s7dcl ---\nd");
        var withoutXml = BlockBundleText.Parse("B.s7dcl", "--- FILE: B.s7dcl ---\nd");

        Assert.Equal("B.XML", BlockBundleText.FindSimaticMlDocument(withXml)?.LogicalName);
        Assert.Null(BlockBundleText.FindSimaticMlDocument(withoutXml));
    }
}
