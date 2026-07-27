using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests;

public class SourceFormatNamesTests
{
    [Fact]
    public void Null_value_uses_the_caller_supplied_fallback()
    {
        var ok = SourceFormatNames.TryNormalize(null, SourceFormatNames.Source, out var normalized, out var error);

        Assert.True(ok);
        Assert.Equal("source", normalized);
        Assert.Null(error);
    }

    [Fact]
    public void Whitespace_value_uses_the_caller_supplied_fallback()
    {
        var ok = SourceFormatNames.TryNormalize("   ", SourceFormatNames.Xml, out var normalized, out var error);

        Assert.True(ok);
        Assert.Equal("xml", normalized);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("source", "source")]
    [InlineData("SOURCE", "source")]
    [InlineData("Source", "source")]
    [InlineData("xml", "xml")]
    [InlineData("XML", "xml")]
    public void Known_values_normalize_case_insensitively(string input, string expected)
    {
        var ok = SourceFormatNames.TryNormalize(input, SourceFormatNames.Xml, out var normalized, out var error);

        Assert.True(ok);
        Assert.Equal(expected, normalized);
        Assert.Null(error);
    }

    [Fact]
    public void Unknown_value_is_rejected_and_lists_the_allowed_values()
    {
        var ok = SourceFormatNames.TryNormalize("s7dcl", SourceFormatNames.Xml, out var normalized, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, normalized);
        Assert.NotNull(error);
        Assert.Contains("s7dcl", error);
        Assert.Contains("source", error);
        Assert.Contains("xml", error);
    }

    [Fact]
    public void Allowed_lists_exactly_the_two_supported_formats()
    {
        Assert.Equal(new[] { "source", "xml" }, SourceFormatNames.Allowed);
    }
}