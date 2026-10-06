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
    public void TagTableNarrowingIsValidOnlyForListTagTables()
    {
        var narrowed = new PlcOperationRequest
        {
            OperationId = "t", Operation = "list_tag_tables", PlcName = "PLC_1", TableName = "Motors", FolderPath = "/Line",
        };
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { narrowed }).IsValid);

        var withTable = Block("b");
        withTable.TableName = "Motors";
        var withFolder = new PlcOperationRequest { OperationId = "c", Operation = "get_type_content", TypePath = "t", FolderPath = "/Line" };
        var error = PlcOperationCatalog.ValidateRead(new[] { withTable, withFolder }).Error;

        Assert.Contains("'tableName' is not valid for get_block_content", error);
        Assert.Contains("'folderPath' is not valid for get_type_content", error);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData(null, " ")]
    public void RejectsBlankTagTableNarrowing(string? tableName, string? folderPath)
    {
        var op = new PlcOperationRequest { OperationId = "t", Operation = "list_tag_tables", TableName = tableName, FolderPath = folderPath };

        Assert.Contains("must be nonblank", PlcOperationCatalog.ValidateRead(new[] { op }).Error);
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
    [InlineData("read_cross_references")]
    [InlineData("nope")]
    public void RejectsUnknownOperationName(string name)
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

    // ---- plc_write validation ----

    private const string XmlHash = "xml:sha256:0000000000000000000000000000000000000000000000000000000000000000";
    private const string SourceHash = "source:sha256:1111111111111111111111111111111111111111111111111111111111111111";

    private static PlcOperationRequest UpdateBlock(string id = "u", string? hash = XmlHash, string? format = null) => new()
    {
        OperationId = id,
        Operation = "update_block_logic",
        BlockPath = "PLC_1/Blocks/Main",
        Content = "<Document/>",
        ExpectedContentHash = hash,
        Format = format,
    };

    private static PlcOperationRequest CreateBlock(string id, string path) => new()
    {
        OperationId = id,
        Operation = "create_block",
        BlockPath = path,
        BlockType = "FB",
    };

    private static PlcOperationRequest Group(string operation, string path) => new()
    {
        OperationId = "g",
        Operation = operation,
        BlockPath = path,
    };

    [Fact]
    public void RejectsReadOperationInWrite()
    {
        var result = PlcOperationCatalog.ValidateWrite(new[] { Block() });

        Assert.False(result.IsValid);
        Assert.Contains("'get_block_content' is a read operation; use plc_read.", result.Error);
    }

    [Fact]
    public void ReadValidationRejectsWriteOperation()
    {
        var result = PlcOperationCatalog.ValidateRead(new[] { UpdateBlock() });

        Assert.False(result.IsValid);
        Assert.Contains("'update_block_logic' is a write operation; use plc_write.", result.Error);
    }

    [Fact]
    public void AcceptsAValidUpdateBlock()
        => Assert.True(PlcOperationCatalog.ValidateWrite(new[] { UpdateBlock() }).IsValid);

    [Fact]
    public void RequiresContentAndExpectedContentHash()
    {
        var op = new PlcOperationRequest { OperationId = "u", Operation = "update_type_content", TypePath = "PLC_1/Types/T" };

        var result = PlcOperationCatalog.ValidateWrite(new[] { op });

        Assert.False(result.IsValid);
        Assert.Contains("missing required field(s): content, expectedContentHash", result.Error);
    }

    [Fact]
    public void RejectsHashFormatMismatch()
    {
        var result = PlcOperationCatalog.ValidateWrite(new[] { UpdateBlock(hash: SourceHash, format: "xml") });

        Assert.False(result.IsValid);
        Assert.Contains("'source'", result.Error);
        Assert.Contains("'xml'", result.Error);
    }

    [Fact]
    public void RejectsHashFormatMismatchAgainstTheDefaultFormat()
    {
        var result = PlcOperationCatalog.ValidateWrite(new[] { UpdateBlock(hash: SourceHash) });

        Assert.False(result.IsValid);
        Assert.Contains("expectedContentHash", result.Error);
    }

    [Theory]
    [InlineData("deadbeef")]
    [InlineData("xml:sha256:ABC")]
    [InlineData("xml:md5:0000000000000000000000000000000000000000000000000000000000000000")]
    public void RejectsMalformedHash(string hash)
    {
        var result = PlcOperationCatalog.ValidateWrite(new[] { UpdateBlock(hash: hash) });

        Assert.False(result.IsValid);
        Assert.Contains("malformed", result.Error);
    }

    [Fact]
    public void RejectsContentOpOnBlockCreatedEarlierInCall()
    {
        var ops = new[] { CreateBlock("c", "PLC_1/Blocks/Main"), UpdateBlock("u") };

        var result = PlcOperationCatalog.ValidateWrite(ops);

        Assert.False(result.IsValid);
        Assert.Contains("created earlier in this call", result.Error);
    }

    [Fact]
    public void AcceptsContentOpOnABlockCreatedLater()
    {
        var ops = new[] { UpdateBlock("u"), CreateBlock("c", "PLC_1/Blocks/Main") };

        Assert.True(PlcOperationCatalog.ValidateWrite(ops).IsValid);
    }

    [Fact]
    public void RejectsDifferingProjectPaths()
    {
        var a = UpdateBlock("a");
        a.ProjectPath = Path.Combine(Path.GetTempPath(), "one.ap21");
        var b = UpdateBlock("b");
        b.ProjectPath = Path.Combine(Path.GetTempPath(), "two.ap21");

        var result = PlcOperationCatalog.ValidateWrite(new[] { a, b });

        Assert.False(result.IsValid);
        Assert.Contains("same project path", result.Error);
    }

    [Theory]
    [InlineData("create_block_group")]
    [InlineData("delete_block_group")]
    public void RejectsOneSegmentGroupPath(string operation)
    {
        var result = PlcOperationCatalog.ValidateWrite(new[] { Group(operation, "Main") });

        Assert.False(result.IsValid);
        Assert.Contains("PLC/Name", result.Error);
    }

    [Fact]
    public void RejectsOneSegmentCreatePath()
    {
        var result = PlcOperationCatalog.ValidateWrite(new[] { CreateBlock("c", "Main") });

        Assert.False(result.IsValid);
        Assert.Contains("PLC/Name", result.Error);
    }

    [Theory]
    [InlineData("create_block_group", "PLC_1/Group")]
    [InlineData("delete_block_group", "PLC_1/Blocks/Group")]
    public void AcceptsTwoSegmentRootGroupPath(string operation, string path)
        => Assert.True(PlcOperationCatalog.ValidateWrite(new[] { Group(operation, path) }).IsValid);

    [Theory]
    [InlineData("PLC_1/Main")]
    [InlineData("PLC_1/Blocks/Group/Main")]
    public void AcceptsTwoSegmentRootCreatePath(string path)
        => Assert.True(PlcOperationCatalog.ValidateWrite(new[] { CreateBlock("c", path) }).IsValid);

    [Fact]
    public void RejectsInapplicableFieldsOnWrites()
    {
        var op = new PlcOperationRequest { OperationId = "d", Operation = "delete_block", BlockPath = "PLC_1/Blocks/B", Content = "x" };

        var result = PlcOperationCatalog.ValidateWrite(new[] { op });

        Assert.False(result.IsValid);
        Assert.Contains("'content' is not valid for delete_block", result.Error);
    }

    [Fact]
    public void WriteSpecsKeepTheBatchFieldSets()
    {
        // Field sets are kept from the batch catalog; yamlContent/sourceContent become content and
        // the two update operations also require expectedContentHash.
        static string Map(string f) => f is "yamlContent" or "sourceContent" ? "content" : f;
        var batchWrites = TiaMcpServer.Batch.BatchOperationCatalog.All
            .Where(spec => spec.Name is not ("start_plc" or "stop_plc"))
            .ToList();
        Assert.Equal(14, batchWrites.Count);
        foreach (var batch in batchWrites)
        {
            Assert.True(PlcOperationCatalog.TryGetWriteFields(batch.Name, out var required, out var optional), batch.Name);
            var expectedRequired = batch.RequiredFields.Select(Map).ToList();
            if (batch.Name is "update_block_logic" or "update_type_content")
            {
                expectedRequired.Add("expectedContentHash");
            }

            Assert.Equal(expectedRequired.OrderBy(x => x), required.OrderBy(x => x));
            Assert.Equal(batch.OptionalFields.OrderBy(x => x), optional.OrderBy(x => x));
        }

        Assert.Equal(14, PlcOperationCatalog.WriteOperationNames.Count);
    }

    [Theory]
    [InlineData("start_plc")]
    [InlineData("stop_plc")]
    public void WriteValidationRejectsPlcRunStopAsUnknown(string name)
    {
        var op = new PlcOperationRequest { OperationId = "a", Operation = name };

        var result = PlcOperationCatalog.ValidateWrite(new[] { op });

        Assert.False(result.IsValid);
        Assert.Contains($"Unknown operation '{name}'", result.Error);
        Assert.False(PlcOperationCatalog.TryGetWriteFields(name, out _, out _));
    }
}
