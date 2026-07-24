using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests;

public class BlockWritePreflightTests
{
    [Fact]
    public void PrepareUpdate_RejectsInvalidBlockPathBeforeParsingBundle()
    {
        const string malformedBundle = "--- FILE:  ---\n<Document />";

        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockWritePreflight.PrepareUpdate("PLC/Blocks/", "fallback.xml", malformedBundle));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
        Assert.Contains("Block path", exception.Message);
    }

    [Fact]
    public void PrepareUpdate_ReturnsParsedAddressAndPrimaryDocument()
    {
        const string bundle = "--- FILE: Main.xml ---\n<Document />";

        var result = BlockWritePreflight.PrepareUpdate(
            "PLC/Blocks/Main",
            "fallback.xml",
            bundle);

        Assert.Equal("PLC", result.Address.PlcName);
        Assert.Equal("Main", result.Address.BlockName);
        Assert.Equal("Main.xml", result.Bundle.PrimaryDocumentName);
    }

    [Theory]
    [InlineData("UNKNOWN", "LAD")]
    [InlineData("DB", "UNKNOWN")]
    public void PrepareCreate_RejectsUnsupportedNormalizedPairAsValidationError(
        string blockType,
        string language)
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockWritePreflight.PrepareCreate(
                "PLC/Blocks/NewBlock",
                blockType,
                language));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Fact]
    public void PrepareCreate_NormalizesPairBeforeReturningAddress()
    {
        var result = BlockWritePreflight.PrepareCreate(
            "PLC/Blocks/NewBlock",
            "fc",
            "scl");

        Assert.Equal("FC", result.BlockType);
        Assert.Equal("SCL", result.Language);
        Assert.Equal("PLC", result.Address.PlcName);
        Assert.Equal("NewBlock", result.Address.BlockName);
    }
}
