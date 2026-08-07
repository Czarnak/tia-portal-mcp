using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class BlockAddressTests
{
    [Fact]
    public void ParseSupportsLegacyBlockOnly()
    {
        var address = BlockAddress.Parse("Main");

        Assert.Null(address.PlcName);
        Assert.Null(address.UnitName);
        Assert.Equal("Main", address.BlockName);
        Assert.Empty(address.FolderPath);
        Assert.False(address.IsDeterministic);
        Assert.False(address.UsesSoftwareUnit);
    }

    [Fact]
    public void ParseSupportsLegacyPlcQualifiedBlock()
    {
        var address = BlockAddress.Parse("PLC_1/Main");

        Assert.Equal("PLC_1", address.PlcName);
        Assert.Null(address.UnitName);
        Assert.Equal("Main", address.BlockName);
        Assert.Empty(address.FolderPath);
        Assert.False(address.IsDeterministic);
    }

    [Fact]
    public void ParseSupportsNestedBlockFolderPath()
    {
        var address = BlockAddress.Parse("PLC_1/Blocks/Motion/Axis/Main");

        Assert.Equal("PLC_1", address.PlcName);
        Assert.Null(address.UnitName);
        Assert.Equal(new[] { "Motion", "Axis" }, address.FolderPath);
        Assert.Equal("Main", address.BlockName);
        Assert.True(address.IsDeterministic);
        Assert.False(address.UsesSoftwareUnit);
    }

    [Fact]
    public void ParseSupportsSoftwareUnitBlockPath()
    {
        var address = BlockAddress.Parse("PLC_1/Units/Line1/Blocks/Motion/Main");

        Assert.Equal("PLC_1", address.PlcName);
        Assert.Equal("Line1", address.UnitName);
        Assert.Equal(new[] { "Motion" }, address.FolderPath);
        Assert.Equal("Main", address.BlockName);
        Assert.True(address.IsDeterministic);
        Assert.True(address.UsesSoftwareUnit);
    }

    [Fact]
    public void ParseStripsBlockSuffixFromFinalSegment()
    {
        var address = BlockAddress.Parse("PLC_1/Blocks/Main [FB1]");

        Assert.Equal("Main", address.BlockName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("PLC_1//Main")]
    [InlineData("PLC_1/Blocks")]
    [InlineData("PLC_1/Units/Unit1/Blocks")]
    [InlineData("PLC_1/Units//Blocks/Main")]
    [InlineData("PLC_1/Units/Unit1/Types/Main")]
    public void ParseRejectsInvalidPaths(string blockPath)
    {
        Assert.Throws<ArgumentException>(() => BlockAddress.Parse(blockPath));
    }
}
