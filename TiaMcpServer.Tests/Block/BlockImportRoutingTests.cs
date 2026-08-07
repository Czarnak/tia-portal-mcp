using System.Collections.Generic;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class BlockImportRoutingTests
{
    private static ParsedBlockImportBundle Bundle(params (string Name, string Content)[] documents)
    {
        var list = new List<BlockImportDocument>();
        foreach (var (name, content) in documents)
        {
            list.Add(new BlockImportDocument(name, name, content));
        }

        return new ParsedBlockImportBundle(list[0].LogicalName, list);
    }

    [Fact]
    public void A_bundle_containing_simatic_ml_xml_routes_to_the_simatic_ml_importer()
    {
        var bundle = Bundle(("Main.xml", "<Document />"), ("Main.s7dcl", "BLOCK\r\n"));

        Assert.Equal(BlockImportRoute.SimaticMl, BlockImportRouting.SelectRoute(bundle));
        Assert.Equal("Main.xml", BlockImportRouting.SelectAuthoritativeDocument(bundle).LogicalName);
    }

    [Fact]
    public void A_bundle_without_xml_routes_to_the_documents_importer()
    {
        var bundle = Bundle(("Main.s7dcl", "BLOCK\r\n"));

        Assert.Equal(BlockImportRoute.SimaticSd, BlockImportRouting.SelectRoute(bundle));
    }

    [Fact]
    public void The_documents_route_uses_an_extension_less_base_name()
    {
        var bundle = Bundle(("Main.s7dcl", "BLOCK\r\n"), ("Main.s7res", "res"));

        Assert.Equal("Main", BlockImportRouting.SimaticSdBaseName(bundle));
    }

    [Fact]
    public void Editing_a_non_authoritative_document_is_rejected()
    {
        var submitted = Bundle(("Main.xml", "<Document />"), ("Main.s7dcl", "EDITED\r\n"));
        var current = Bundle(("Main.xml", "<Document />"), ("Main.s7dcl", "BLOCK\r\n"));

        var exception = Assert.Throws<WorkerOperationException>(() =>
            BlockImportRouting.EnsureOnlyAuthoritativeDocumentChanged(submitted, current, "Main.xml"));

        Assert.Contains("Main.s7dcl", exception.Message);
        Assert.Contains("Main.xml", exception.Message);
    }

    [Fact]
    public void Editing_the_authoritative_document_is_allowed()
    {
        var submitted = Bundle(("Main.xml", "<Document>edited</Document>"), ("Main.s7dcl", "BLOCK\r\n"));
        var current = Bundle(("Main.xml", "<Document />"), ("Main.s7dcl", "BLOCK\r\n"));

        BlockImportRouting.EnsureOnlyAuthoritativeDocumentChanged(submitted, current, "Main.xml");
    }
}