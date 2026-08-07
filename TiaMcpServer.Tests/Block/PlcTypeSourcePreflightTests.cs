using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class PlcTypeSourcePreflightTests
{
    private static string FixturePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void Reads_the_type_name_from_a_real_V21_udt_export()
    {
        var content = File.ReadAllText(FixturePath("AnalogInputSettings.udt"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Source, SourceObjectKind.Type, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("AnalogInputSettings", name);
    }

    [Fact]
    public void Reads_the_block_name_from_a_real_V21_db_export()
    {
        var content = File.ReadAllText(FixturePath("Simulation_DB.db"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Source, SourceObjectKind.DataBlock, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("Simulation_DB", name);
    }

    [Fact]
    public void Reads_the_name_from_a_real_V21_SimaticML_export()
    {
        var content = File.ReadAllText(FixturePath("AnalogInputSettings.xml"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Xml, SourceObjectKind.Type, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("AnalogInputSettings", name);
    }

    [Fact]
    public void Accepts_an_unquoted_type_name()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "TYPE Foo\nSTRUCT\nEND_STRUCT;\nEND_TYPE\n",
            SourceFormatNames.Source, SourceObjectKind.Type, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("Foo", name);
    }

    [Fact]
    public void Skips_leading_comments_and_blank_lines()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "// generated\r\n\r\n(* banner *)\r\nTYPE \"Foo\"\r\nEND_TYPE\r\n",
            SourceFormatNames.Source, SourceObjectKind.Type, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("Foo", name);
    }

    [Fact]
    public void Skips_a_leading_attribute_block_before_DATA_BLOCK()
    {
        var content = "{ DB_Accessible_From_OPC_UA := 'FALSE' }\r\nDATA_BLOCK \"Bar\"\r\nEND_DATA_BLOCK\r\n";

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Source, SourceObjectKind.DataBlock, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("Bar", name);
    }

    [Fact]
    public void Empty_content_is_rejected()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "   ", SourceFormatNames.Source, SourceObjectKind.Type, out var name, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, name);
        Assert.NotNull(error);
    }

    [Fact]
    public void Xml_with_no_name_element_is_rejected()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "<Document><SW.Types.PlcStruct /></Document>",
            SourceFormatNames.Xml, SourceObjectKind.Type, out var name, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void Malformed_xml_is_rejected_without_throwing()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "<Document><unclosed>", SourceFormatNames.Xml, SourceObjectKind.Type, out var name, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void A_function_block_submitted_to_a_type_write_is_rejected_by_kind()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "FUNCTION_BLOCK \"Nope\"\r\nEND_FUNCTION_BLOCK\r\n",
            SourceFormatNames.Source, SourceObjectKind.Type, out var name, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, name);
        Assert.NotNull(error);
        Assert.Contains("FUNCTION_BLOCK", error);
        Assert.Contains("TYPE", error);
    }

    [Fact]
    public void Source_declaring_nothing_is_rejected_and_lists_the_expected_keywords()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "VAR\r\n  Foo : Bool;\r\nEND_VAR\r\n",
            SourceFormatNames.Source, SourceObjectKind.Type, out var name, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, name);
        Assert.NotNull(error);
        Assert.Contains("TYPE", error);
        Assert.Contains("FUNCTION_BLOCK", error);
    }

    [Fact]
    public void A_two_object_source_is_rejected_and_names_both_objects()
    {
        var content = File.ReadAllText(FixturePath("AnalogInput.scl"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Source, SourceObjectKind.FunctionBlock, out var name, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, name);
        Assert.NotNull(error);
        Assert.Contains("2 objects", error);
        Assert.Contains("AnalogInputSettings", error);
        Assert.Contains("AnalogInput", error);
    }

    [Fact]
    public void A_four_object_source_is_rejected_and_names_every_object()
    {
        var content = File.ReadAllText(FixturePath("DamperAnalog.scl"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Source, SourceObjectKind.FunctionBlock, out _, out var error);

        Assert.False(ok);
        Assert.Contains("4 objects", error);
        Assert.Contains("HMI_Settings_DB", error);
        Assert.Contains("UDT_Settings", error);
    }

    [Fact]
    public void A_single_object_scl_source_is_accepted_for_a_function_block_write()
    {
        var content = File.ReadAllText(FixturePath("DamperDigital.scl"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Source, SourceObjectKind.FunctionBlock, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("DamperDigital", name);
    }

    [Fact]
    public void The_xml_path_ignores_the_expected_kind()
    {
        var content = File.ReadAllText(FixturePath("AnalogInputSettings.xml"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Xml, SourceObjectKind.FunctionBlock, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("AnalogInputSettings", name);
    }
}