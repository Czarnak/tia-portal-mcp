using TiaMcpServer.Cli;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

public class PayloadBudgetParserTests
{
    private static PayloadBudgetParseResult Parse(string[] args, string? item = null, string? document = null)
        => PayloadBudgetParser.Parse(args, name => name switch
        {
            "TIA_MCP_MAX_ITEM_CHARS" => item,
            "TIA_MCP_MAX_DOCUMENT_CHARS" => document,
            _ => null
        });

    [Fact]
    public void DefaultsWhenNothingIsConfigured()
    {
        var result = Parse([]);

        Assert.True(result.IsValid);
        Assert.Equal(60_000, result.ItemChars);
        Assert.Equal(180_000, result.DocumentChars);
    }

    [Fact]
    public void ReadsBothFlagForms()
    {
        var result = Parse(["--max-item-chars", "100000", "--max-document-chars=300000"]);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(100_000, result.ItemChars);
        Assert.Equal(300_000, result.DocumentChars);
    }

    [Fact]
    public void FallsBackToEnvironmentPerValue()
    {
        var result = Parse(["--max-document-chars", "400000"], item: "90000", document: "200000");

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(90_000, result.ItemChars);
        Assert.Equal(400_000, result.DocumentChars);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1e5")]
    [InlineData("-70000")]
    [InlineData("59999")]
    [InlineData("1000001")]
    public void RejectsInvalidItemValues(string value)
    {
        var result = Parse(["--max-item-chars", value]);

        Assert.False(result.IsValid);
        Assert.Contains("--max-item-chars", result.Error);
    }

    [Fact]
    public void RejectsInvalidEnvironmentValueNamingTheVariable()
    {
        var result = Parse([], document: "100");

        Assert.False(result.IsValid);
        Assert.Contains("TIA_MCP_MAX_DOCUMENT_CHARS", result.Error);
    }

    [Fact]
    public void RejectsItemLimitAboveDocumentLimit()
    {
        var result = Parse(["--max-item-chars=200000"]);

        Assert.False(result.IsValid);
        Assert.Contains("--max-document-chars", result.Error);
    }

    [Theory]
    [InlineData("--max-item-chars")]
    [InlineData("--max-item-chars", "--read-only")]
    [InlineData("--max-item-chars=")]
    public void RejectsMissingValue(params string[] args)
    {
        var result = Parse(args);

        Assert.False(result.IsValid);
        Assert.Contains("requires a value", result.Error);
    }

    [Fact]
    public void RejectsRepeatedFlag()
    {
        var result = Parse(["--max-item-chars", "70000", "--max-item-chars=80000"]);

        Assert.False(result.IsValid);
        Assert.Contains("only once", result.Error);
    }
}
