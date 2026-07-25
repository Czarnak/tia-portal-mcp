using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests;

public class BlockXmlSanitizerTests
{
    private const string WithDocumentInfo =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
        "<Document>\r\n" +
        "  <Engineering version=\"V21\" />\r\n" +
        "  <DocumentInfo>\r\n" +
        "    <Created>2026-07-25T10:00:00.1234567Z</Created>\r\n" +
        "  </DocumentInfo>\r\n" +
        "  <SW.Blocks.OB ID=\"0\" />\r\n" +
        "</Document>";

    [Fact]
    public void DocumentInfo_is_removed()
    {
        var result = BlockXmlSanitizer.RemoveDocumentInfo(WithDocumentInfo);

        Assert.DoesNotContain("DocumentInfo", result);
        Assert.DoesNotContain("Created", result);
    }

    [Fact]
    public void The_xml_declaration_survives()
    {
        var result = BlockXmlSanitizer.RemoveDocumentInfo(WithDocumentInfo);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", result);
    }

    [Fact]
    public void Every_other_byte_survives_including_indentation_and_line_endings()
    {
        var result = BlockXmlSanitizer.RemoveDocumentInfo(WithDocumentInfo);

        Assert.Equal(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<Document>\r\n" +
            "  <Engineering version=\"V21\" />\r\n" +
            "  <SW.Blocks.OB ID=\"0\" />\r\n" +
            "</Document>",
            result);
    }

    [Fact]
    public void A_self_closing_DocumentInfo_is_removed()
    {
        var result = BlockXmlSanitizer.RemoveDocumentInfo(
            "<Document>\r\n  <DocumentInfo />\r\n  <SW.Blocks.OB ID=\"0\" />\r\n</Document>");

        Assert.Equal(
            "<Document>\r\n  <SW.Blocks.OB ID=\"0\" />\r\n</Document>",
            result);
    }

    [Fact]
    public void Xml_without_DocumentInfo_is_returned_unchanged()
    {
        const string xml = "<Document>\r\n  <SW.Blocks.OB ID=\"0\" />\r\n</Document>";

        Assert.Equal(xml, BlockXmlSanitizer.RemoveDocumentInfo(xml));
    }

    [Fact]
    public void Removal_is_idempotent()
    {
        var once = BlockXmlSanitizer.RemoveDocumentInfo(WithDocumentInfo);

        Assert.Equal(once, BlockXmlSanitizer.RemoveDocumentInfo(once));
    }
}