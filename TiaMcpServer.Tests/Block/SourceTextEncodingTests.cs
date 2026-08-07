using System.Text;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class SourceTextEncodingTests
{
    private static string FixturePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void ForTransport_strips_a_leading_byte_order_mark()
    {
        var withBom = "\uFEFFTYPE \"AnalogInputSettings\"\r\nEND_TYPE\r\n";

        var result = SourceTextEncoding.ForTransport(withBom);

        Assert.StartsWith("TYPE", result);
        // Ordinal is required: U+FEFF carries no collation weight, so a culture-sensitive
        // DoesNotContain reports a match at index 0 of every string.
        Assert.DoesNotContain("\uFEFF", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ForTransport_preserves_CRLF_line_endings()
    {
        var withBom = "\uFEFFTYPE\r\nEND_TYPE\r\n";

        var result = SourceTextEncoding.ForTransport(withBom);

        Assert.Equal("TYPE\r\nEND_TYPE\r\n", result);
    }

    [Fact]
    public void ForTransport_leaves_text_without_a_BOM_untouched()
    {
        var result = SourceTextEncoding.ForTransport("TYPE\r\nEND_TYPE\r\n");

        Assert.Equal("TYPE\r\nEND_TYPE\r\n", result);
    }

    [Fact]
    public void ForFile_writes_a_byte_order_mark()
    {
        var bytes = SourceTextEncoding.ForFile("TYPE\r\nEND_TYPE\r\n");

        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public void ForFile_normalizes_bare_LF_to_CRLF()
    {
        var bytes = SourceTextEncoding.ForFile("TYPE\nEND_TYPE\n");
        var text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);

        Assert.Equal("TYPE\r\nEND_TYPE\r\n", text);
    }

    [Fact]
    public void ForFile_does_not_double_up_existing_CRLF()
    {
        var bytes = SourceTextEncoding.ForFile("TYPE\r\nEND_TYPE\r\n");
        var text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);

        Assert.Equal("TYPE\r\nEND_TYPE\r\n", text);
        Assert.DoesNotContain("\r\r", text);
    }

    [Fact]
    public void ForFile_does_not_emit_a_second_BOM_when_the_transport_text_still_has_one()
    {
        var bytes = SourceTextEncoding.ForFile("\uFEFFTYPE\r\n");
        var text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);

        Assert.StartsWith("TYPE", text);
    }

    [Fact]
    public void Real_V21_udt_export_round_trips_byte_identically()
    {
        var original = File.ReadAllBytes(FixturePath("AnalogInputSettings.udt"));
        var fileText = new UTF8Encoding(true).GetString(original);

        var roundTripped = SourceTextEncoding.ForFile(SourceTextEncoding.ForTransport(fileText));

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void Real_V21_db_export_round_trips_byte_identically()
    {
        var original = File.ReadAllBytes(FixturePath("Simulation_DB.db"));
        var fileText = new UTF8Encoding(true).GetString(original);

        var roundTripped = SourceTextEncoding.ForFile(SourceTextEncoding.ForTransport(fileText));

        Assert.Equal(original, roundTripped);
    }
}