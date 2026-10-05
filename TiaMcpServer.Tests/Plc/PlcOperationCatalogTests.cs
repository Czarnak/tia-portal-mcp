using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

public class PlcOperationCatalogTests
{
    private static PlcOperationRequest Block(string id = "a") => new()
    {
        OperationId = id,
        Operation = "get_block_content",
        BlockPath = "PLC_1/Main",
    };

    [Fact]
    public void AcceptsEachReadOperation()
    {
        var ops = new[]
        {
            Block("a"),
            new PlcOperationRequest { OperationId = "b", Operation = "get_type_content", TypePath = "PLC_1/Types/T", Format = "XML", WithDependencies = true },
            new PlcOperationRequest { OperationId = "c", Operation = "list_tag_tables", PlcName = "PLC_1" },
        };

        Assert.True(PlcOperationCatalog.ValidateRead(ops).IsValid);
    }

    [Fact]
    public void RejectsMoreThanFiftyItems()
    {
        var ops = Enumerable.Range(0, PlcOperationCatalog.MaxBatchSize + 1).Select(i => Block($"op{i}")).ToArray();

        var result = PlcOperationCatalog.ValidateRead(ops);

        Assert.False(result.IsValid);
        Assert.Contains("maximum of 50", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void RejectsDuplicateOrBlankOperationId(string blank)
    {
        Assert.False(PlcOperationCatalog.ValidateRead(new[] { Block(blank) }).IsValid);
        var duplicate = PlcOperationCatalog.ValidateRead(new[] { Block("x"), Block("x") });
        Assert.False(duplicate.IsValid);
        Assert.Contains("Duplicate operationId 'x'", duplicate.Error);
    }

    [Fact]
    public void RejectsOperationIdOver256Chars()
    {
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { Block(new string('i', 256)) }).IsValid);
        Assert.False(PlcOperationCatalog.ValidateRead(new[] { Block(new string('i', 257)) }).IsValid);
    }

    [Fact]
    public void RejectsInapplicableField()
    {
        var op = Block();
        op.TypePath = "PLC_1/Types/T";

        var result = PlcOperationCatalog.ValidateRead(new[] { op });

        Assert.False(result.IsValid);
        Assert.Contains("'typePath' is not valid for get_block_content", result.Error);
    }

    [Fact]
    public void RejectsMissingRequiredFieldAndBadFormat()
    {
        var missing = new PlcOperationRequest { OperationId = "a", Operation = "get_type_content" };
        Assert.Contains("missing required field(s): typePath", PlcOperationCatalog.ValidateRead(new[] { missing }).Error);

        var badFormat = Block();
        badFormat.Format = "yaml";
        Assert.Contains("Invalid format 'yaml'", PlcOperationCatalog.ValidateRead(new[] { badFormat }).Error);
    }

    [Fact]
    public void RejectsUnknownMember()
    {
        const string json = """{"operationId":"a","operation":"get_block_content","blockPath":"x","bogus":1}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<PlcOperationRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Theory]
    [InlineData("update_block_logic")]
    [InlineData("create_tag")]
    [InlineData("read_cross_references")]
    public void RejectsWriteOperationName(string name)
    {
        var op = new PlcOperationRequest { OperationId = "a", Operation = name };

        var result = PlcOperationCatalog.ValidateRead(new[] { op });

        Assert.False(result.IsValid);
        Assert.Contains($"Unknown operation '{name}'", result.Error);
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly)]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public void EveryReadOperationIsAllowedInEveryMode(McpAccessMode mode)
    {
        var ops = new[]
        {
            Block(),
            new PlcOperationRequest { OperationId = "b", Operation = "get_type_content", TypePath = "t" },
            new PlcOperationRequest { OperationId = "c", Operation = "list_tag_tables" },
        };

        Assert.Empty(PlcOperationCatalog.ValidateAccessMode(ops, mode));
    }
}
