using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class BlockDocumentPackagePolicyTests
{
    private const string S7300Detail =
        "Error when calling method 'ExportAsDocuments' of type 'Siemens.Engineering.SW.Blocks.FC'.\r\n\r\n"
        + "CPUs of the S7-300/S7-400 series do not support the document export or import of blocks "
        + "or PLC data types.";

    [Fact]
    public void Surviving_xml_degrades_to_a_warning_rather_than_failing_the_read()
    {
        var outcome = BlockDocumentPackagePolicy.Decide(
            hasPrimaryXmlDocument: true,
            displayPath: "PLC_1/Blocks/10_Alarms/FC - Alarms",
            failureDetail: S7300Detail);

        Assert.False(outcome.IsFatal);
        Assert.Contains("PLC_1/Blocks/10_Alarms/FC - Alarms", outcome.Message);
    }

    [Fact]
    public void Degraded_message_says_the_payload_is_still_usable_for_writes()
    {
        var outcome = BlockDocumentPackagePolicy.Decide(
            hasPrimaryXmlDocument: true,
            displayPath: "PLC_1/Blocks/Main",
            failureDetail: S7300Detail);

        Assert.Contains("update_block_logic", outcome.Message);
    }

    [Fact]
    public void No_surviving_document_is_fatal_because_the_bundle_would_be_empty()
    {
        var outcome = BlockDocumentPackagePolicy.Decide(
            hasPrimaryXmlDocument: false,
            displayPath: "PLC_1/Blocks/Main",
            failureDetail: S7300Detail);

        Assert.True(outcome.IsFatal);
        Assert.Contains("PLC_1/Blocks/Main", outcome.Message);
    }

    [Fact]
    public void Fatal_message_names_both_failed_exports_so_the_cause_is_not_ambiguous()
    {
        var outcome = BlockDocumentPackagePolicy.Decide(
            hasPrimaryXmlDocument: false,
            displayPath: "PLC_1/Blocks/Main",
            failureDetail: "boom");

        Assert.Contains("Simatic ML XML", outcome.Message);
        Assert.Contains("s7dcl", outcome.Message);
    }

    [Fact]
    public void Detail_is_collapsed_to_one_line_so_it_arrives_as_a_single_warning()
    {
        // The worker splits captured stderr on newlines; a multi-line detail would otherwise
        // surface as several unrelated-looking warnings.
        var outcome = BlockDocumentPackagePolicy.Decide(
            hasPrimaryXmlDocument: true,
            displayPath: "PLC_1/Blocks/Main",
            failureDetail: S7300Detail);

        Assert.DoesNotContain("\n", outcome.Message);
        Assert.DoesNotContain("\r", outcome.Message);
    }

    [Fact]
    public void Long_detail_is_bounded_so_one_warning_cannot_dominate_the_list()
    {
        var outcome = BlockDocumentPackagePolicy.Decide(
            hasPrimaryXmlDocument: true,
            displayPath: "PLC_1/Blocks/Main",
            failureDetail: new string('x', 4000));

        Assert.True(outcome.Message.Length < 1024);
        Assert.Contains("…", outcome.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Missing_detail_still_produces_an_actionable_message(string? detail)
    {
        var outcome = BlockDocumentPackagePolicy.Decide(
            hasPrimaryXmlDocument: true,
            displayPath: "PLC_1/Blocks/Main",
            failureDetail: detail!);

        Assert.False(outcome.IsFatal);
        Assert.Contains("no detail", outcome.Message);
    }
}
