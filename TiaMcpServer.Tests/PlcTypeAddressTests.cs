using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests;

public class PlcTypeAddressTests
{
    [Fact]
    public void Bare_name_is_non_deterministic_with_no_plc()
    {
        var address = PlcTypeAddress.Parse("AnalogInputSettings");

        Assert.Null(address.PlcName);
        Assert.Null(address.UnitName);
        Assert.Empty(address.FolderPath);
        Assert.Equal("AnalogInputSettings", address.TypeName);
        Assert.False(address.IsDeterministic);
    }

    [Fact]
    public void Plc_and_name_is_non_deterministic_with_a_plc()
    {
        var address = PlcTypeAddress.Parse("PLC_1/AnalogInputSettings");

        Assert.Equal("PLC_1", address.PlcName);
        Assert.Null(address.UnitName);
        Assert.Empty(address.FolderPath);
        Assert.Equal("AnalogInputSettings", address.TypeName);
        Assert.False(address.IsDeterministic);
    }

    [Fact]
    public void Types_segment_makes_the_address_deterministic()
    {
        var address = PlcTypeAddress.Parse("PLC_1/Types/AnalogInputSettings");

        Assert.Equal("PLC_1", address.PlcName);
        Assert.Null(address.UnitName);
        Assert.Empty(address.FolderPath);
        Assert.Equal("AnalogInputSettings", address.TypeName);
        Assert.True(address.IsDeterministic);
    }

    [Fact]
    public void Nested_folders_under_Types_are_captured_in_order()
    {
        var address = PlcTypeAddress.Parse("PLC_1/Types/Sensors/Analog/AnalogInputSettings");

        Assert.Equal("PLC_1", address.PlcName);
        Assert.Equal(new[] { "Sensors", "Analog" }, address.FolderPath);
        Assert.Equal("AnalogInputSettings", address.TypeName);
        Assert.True(address.IsDeterministic);
    }

    [Fact]
    public void Software_unit_path_captures_the_unit_name()
    {
        var address = PlcTypeAddress.Parse("PLC_1/Units/DriveUnit/Types/Sensors/AnalogInputSettings");

        Assert.Equal("PLC_1", address.PlcName);
        Assert.Equal("DriveUnit", address.UnitName);
        Assert.True(address.UsesSoftwareUnit);
        Assert.Equal(new[] { "Sensors" }, address.FolderPath);
        Assert.Equal("AnalogInputSettings", address.TypeName);
        Assert.True(address.IsDeterministic);
    }

    [Fact]
    public void Types_segment_is_matched_case_insensitively()
    {
        var address = PlcTypeAddress.Parse("PLC_1/types/AnalogInputSettings");

        Assert.True(address.IsDeterministic);
        Assert.Equal("AnalogInputSettings", address.TypeName);
    }

    [Fact]
    public void Segments_are_trimmed()
    {
        var address = PlcTypeAddress.Parse(" PLC_1 / Types / AnalogInputSettings ");

        Assert.Equal("PLC_1", address.PlcName);
        Assert.Equal("AnalogInputSettings", address.TypeName);
    }

    [Fact]
    public void Round_trips_through_ToDisplayPath()
    {
        var address = PlcTypeAddress.Parse("PLC_1/Units/DriveUnit/Types/Sensors/AnalogInputSettings");

        Assert.Equal("PLC_1/Units/DriveUnit/Types/Sensors/AnalogInputSettings", address.ToDisplayPath());
    }

    [Fact]
    public void Non_deterministic_display_path_omits_the_Types_segment()
    {
        var address = PlcTypeAddress.Parse("PLC_1/AnalogInputSettings");

        Assert.Equal("PLC_1/AnalogInputSettings", address.ToDisplayPath());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_path_is_rejected(string input)
    {
        Assert.Throws<ArgumentException>(() => PlcTypeAddress.Parse(input));
    }

    [Fact]
    public void Empty_segment_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => PlcTypeAddress.Parse("PLC_1//AnalogInputSettings"));
    }

    [Fact]
    public void Types_segment_with_no_type_name_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => PlcTypeAddress.Parse("PLC_1/Types"));
    }

    [Fact]
    public void A_blocks_path_is_rejected_because_it_is_not_a_type_path()
    {
        var ex = Assert.Throws<ArgumentException>(() => PlcTypeAddress.Parse("PLC_1/Blocks/Main"));

        Assert.Contains("Types", ex.Message);
    }
}