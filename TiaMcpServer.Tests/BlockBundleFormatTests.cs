using System.Collections.Generic;
using System.IO;
using System.Linq;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests;

public class BlockBundleFormatTests
{
    private static BlockImportDocument Doc(string name, string content)
        => new BlockImportDocument(name, name, content);

    [Fact]
    public void Compose_inserts_a_newline_before_every_marker_after_the_first()
    {
        var composed = BlockBundleFormat.Compose(new List<BlockImportDocument>
        {
            Doc("Main.xml", "<Document />"),      // no trailing newline
            Doc("Main.s7dcl", "BLOCK\r\n"),
        });

        Assert.Contains("<Document />\n--- FILE: Main.s7dcl ---\n", composed);
    }

    [Fact]
    public void Parse_recovers_every_document_from_Compose_output()
    {
        var documents = new List<BlockImportDocument>
        {
            Doc("Main.xml", "<Document />"),
            Doc("Main.s7dcl", "BLOCK\r\n"),
            Doc("Main.s7res", "res"),
        };

        var parsed = BlockImportBundleParser.Parse("Main.xml", BlockBundleFormat.Compose(documents));

        Assert.Equal(3, parsed.Documents.Count);
        Assert.Equal(
            new[] { "Main.xml", "Main.s7dcl", "Main.s7res" },
            parsed.Documents.Select(d => d.LogicalName).ToArray());
    }

    [Fact]
    public void Compose_is_stable_under_a_parse_round_trip()
    {
        var documents = new List<BlockImportDocument>
        {
            Doc("Main.xml", "<Document />"),
            Doc("Main.s7dcl", "BLOCK\r\nEND_BLOCK\r\n"),
        };

        var once = BlockBundleFormat.Compose(documents);
        var twice = BlockBundleFormat.Compose(BlockImportBundleParser.Parse("Main.xml", once).Documents);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Compose_rejects_content_that_would_parse_as_a_delimiter()
    {
        var documents = new List<BlockImportDocument>
        {
            Doc("Main.xml", "<Document />\n--- FILE: Injected.xml ---\nevil"),
        };

        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockBundleFormat.Compose(documents));
        Assert.Contains("delimiter", exception.Message);
    }

    [Fact]
    public void Real_captured_get_block_content_output_parses_into_both_documents()
    {
        var raw = File.ReadAllText(
            Path.Combine("Fixtures", "get_block_content.ob-lad.bundle.txt"));

        var parsed = BlockImportBundleParser.Parse("Main.xml", raw);

        Assert.Equal(2, parsed.Documents.Count);
        Assert.Equal("Main.xml", parsed.Documents[0].LogicalName);
        Assert.Equal("Main.s7dcl", parsed.Documents[1].LogicalName);
        Assert.DoesNotContain("--- FILE:", parsed.Documents[0].Content);
    }
}