using TiaMcpServer.Contracts.Vci;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public class VciFormatsTests
{
    [Theory]
    [InlineData("SCL", new[] { "SimaticML", "SCL" }, true, "SCL", false)]
    [InlineData("SCL", new[] { "SimaticML" }, true, "SimaticML", true)]
    [InlineData(null, new[] { "SimaticSD" }, false, "SimaticSD", true)]
    [InlineData(null, new[] { "SimaticSD" }, true, null, false)]
    [InlineData("simaticsd", new[] { "SimaticML", "SimaticSD" }, true, "SimaticSD", false)]
    [InlineData("UDT", new[] { "UDT" }, true, "UDT", false)]
    [InlineData(null, new[] { "SimaticML", "SCL" }, true, "SimaticML", false)]
    [InlineData("unknown", new[] { "SIMATICML", "SimaticSD" }, true, "SIMATICML", true)]
    [InlineData("unknown", new[] { "UDT" }, true, "UDT", true)]
    [InlineData("unknown", new[] { "simaticsd" }, true, null, false)]
    [InlineData(" SCL ", new[] { "SimaticML", "SCL" }, true, "SimaticML", true)]
    [InlineData(null, new[] { "SCL", "DB" }, true, null, false)]
    public void Resolve_UsesSupportedSpellingAndCountsOnlyFormatSubstitution(
        string? requested, string[] supported, bool isPlcLeaf, string? expectedFormat, bool expectedFallback)
    {
        var choice = VciFormats.Resolve(requested, supported, isPlcLeaf);
        Assert.Equal(expectedFormat, choice.Format);
        Assert.Equal(expectedFallback, choice.IsFallback);
    }

    [Fact]
    public void NoSupportedFormats_ReturnsNoChoice()
    {
        var choice = VciFormats.Resolve(null, Array.Empty<string>(), true);
        Assert.Null(choice.Format);
        Assert.False(choice.IsFallback);
    }
}
