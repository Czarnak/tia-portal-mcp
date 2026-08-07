using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

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

    [Fact]
    public void GlobalDb_defaults_to_the_DB_language_when_none_is_supplied()
    {
        var preflight = BlockWritePreflight.PrepareCreate("PLC_1/Blocks/MyDb", "GlobalDB", language: null);

        Assert.Equal("GLOBALDB", preflight.BlockType);
        Assert.Equal("DB", preflight.Language);
    }

    [Fact]
    public void GlobalDb_accepts_an_explicit_DB_language()
    {
        var preflight = BlockWritePreflight.PrepareCreate("PLC_1/Blocks/MyDb", "GlobalDB", language: "DB");

        Assert.Equal("DB", preflight.Language);
    }

    [Fact]
    public void GlobalDb_rejects_a_ladder_language()
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockWritePreflight.PrepareCreate("PLC_1/Blocks/MyDb", "GlobalDB", language: "LAD"));

        Assert.Contains("GLOBALDB", exception.Message);
    }
}
