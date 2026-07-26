using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests;

public class PlcTypeSourcePreflightTests
{
    private static string FixturePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void Reads_the_type_name_from_a_real_V21_udt_export()
    {
        var content = File.ReadAllText(FixturePath("AnalogInputSettings.udt"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Source, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("AnalogInputSettings", name);
    }

    [Fact]
    public void Reads_the_block_name_from_a_real_V21_db_export()
    {
        var content = File.ReadAllText(FixturePath("Simulation_DB.db"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Source, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("Simulation_DB", name);
    }

    [Fact]
    public void Reads_the_name_from_a_real_V21_SimaticML_export()
    {
        var content = File.ReadAllText(FixturePath("AnalogInputSettings.xml"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Xml, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("AnalogInputSettings", name);
    }

    [Fact]
    public void Accepts_an_unquoted_type_name()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "TYPE Foo\nSTRUCT\nEND_STRUCT;\nEND_TYPE\n",
            SourceFormatNames.Source, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("Foo", name);
    }

    [Fact]
    public void Skips_leading_comments_and_blank_lines()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "// generated\r\n\r\n(* banner *)\r\nTYPE \"Foo\"\r\nEND_TYPE\r\n",
            SourceFormatNames.Source, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("Foo", name);
    }

    [Fact]
    public void Skips_a_leading_attribute_block_before_DATA_BLOCK()
    {
        var content = "{ DB_Accessible_From_OPC_UA := 'FALSE' }\r\nDATA_BLOCK \"Bar\"\r\nEND_DATA_BLOCK\r\n";

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Source, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("Bar", name);
    }

    [Fact]
    public void Empty_content_is_rejected()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "   ", SourceFormatNames.Source, out var name, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, name);
        Assert.NotNull(error);
    }

    [Fact]
    public void Source_with_no_recognizable_declaration_is_rejected_with_a_useful_message()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "FUNCTION_BLOCK \"Nope\"\nEND_FUNCTION_BLOCK\n",
            SourceFormatNames.Source, out var name, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, name);
        Assert.NotNull(error);
        Assert.Contains("TYPE", error);
        Assert.Contains("DATA_BLOCK", error);
    }

    [Fact]
    public void Xml_with_no_name_element_is_rejected()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "<Document><SW.Types.PlcStruct /></Document>",
            SourceFormatNames.Xml, out var name, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void Malformed_xml_is_rejected_without_throwing()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "<Document><unclosed>", SourceFormatNames.Xml, out var name, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}