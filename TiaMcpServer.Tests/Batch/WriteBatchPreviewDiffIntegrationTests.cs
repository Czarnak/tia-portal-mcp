using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class WriteBatchPreviewDiffIntegrationTests
{
    private const string SourceBlockCurrent =
        "DATA_BLOCK \"Recipe\"\r\nSTRUCT\r\nEND_STRUCT;\r\nBEGIN\r\nEND_DATA_BLOCK\r\n";
    private const string TypeCurrent =
        "TYPE AnalogInputSettings STRUCT Value : Real; END_STRUCT END_TYPE";

    private static OpennessWorkerClient CreateClient(ProjectSessionBinding binding)
        => new(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());

    private static WriteSafetyService CreateSafety(TempAuditDirectory audit, ProjectSessionBinding binding)
        => new(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);

    private static async Task<(OpennessWorkerClient Client, WriteSafetyService Safety, ProjectSessionBinding Binding)> BoundAsync(
        TempAuditDirectory audit,
        string projectPath)
    {
        var binding = new ProjectSessionBinding(null);
        var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, projectPath);
        return (client, CreateSafety(audit, binding), binding);
    }

    [Fact]
    public async Task PreviewWriteBatch_UpdateBlockLogicWithSourceFormat_EmitsStructuredDiff()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "block-source-roundtrip");
        using (client)
        {
            var result = await WriteBatchTools.PreviewWriteBatch(client, safety, new[]
            {
                new BatchOperationRequest { OperationId = "block-source", Operation = "update_block_logic", ProjectPath = "block-source-roundtrip", BlockPath = "PLC_1/Blocks/Recipe_DB", Format = SourceFormatNames.Source, YamlContent = "DATA_BLOCK \"Recipe\"\r\nSTRUCT\r\n  Value : Int;\r\nEND_STRUCT;\r\nBEGIN\r\nEND_DATA_BLOCK\r\n" }
            });
            using var doc = JsonDocument.Parse(result);
            var entry = doc.RootElement.GetProperty("diff").GetProperty("operations")[0];
            Assert.Equal("block-source", entry.GetProperty("operationId").GetString());
            Assert.Equal(SourceFormatNames.Source, entry.GetProperty("format").GetString());
            Assert.True(entry.GetProperty("requested").GetProperty("excerpt").GetProperty("lines").GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task PreviewWriteBatch_UpdateBlockLogicWithoutFormat_DefaultsToXmlAndEmitsStructuredDiff()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "echo");
        using (client)
        {
            var result = await WriteBatchTools.PreviewWriteBatch(client, safety, new[]
            {
                new BatchOperationRequest { OperationId = "block-default", Operation = "update_block_logic", ProjectPath = "echo", BlockPath = "PLC_1/Blocks/Main", YamlContent = "--- FILE: Main.xml\r\n<Document />\r\n" }
            });
            using var doc = JsonDocument.Parse(result);
            var entry = doc.RootElement.GetProperty("diff").GetProperty("operations")[0];
            Assert.Equal("block-default", entry.GetProperty("operationId").GetString());
            Assert.Equal(SourceFormatNames.Xml, entry.GetProperty("format").GetString());
            Assert.True(entry.GetProperty("current").GetProperty("characterCount").GetInt32() > 0);
        }
    }

    [Fact]
    public async Task PreviewWriteBatch_UpdateBlockLogicWithExplicitXmlFormat_EmitsStructuredDiff()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "echo");
        using (client)
        {
            var result = await WriteBatchTools.PreviewWriteBatch(client, safety, new[]
            {
                new BatchOperationRequest { OperationId = "block-xml", Operation = "update_block_logic", ProjectPath = "echo", BlockPath = "PLC_1/Blocks/Main", Format = SourceFormatNames.Xml, YamlContent = "--- FILE: Main.xml\r\n<Document Id=\"next\" />\r\n" }
            });
            using var doc = JsonDocument.Parse(result);
            var entry = doc.RootElement.GetProperty("diff").GetProperty("operations")[0];
            Assert.Equal("block-xml", entry.GetProperty("operationId").GetString());
            Assert.Equal(SourceFormatNames.Xml, entry.GetProperty("format").GetString());
            Assert.True(entry.GetProperty("requested").GetProperty("characterCount").GetInt32() > 0);
        }
    }

    [Fact]
    public async Task PreviewWriteBatch_UpdateTypeContent_EmitsStructuredDiff()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "type-content-roundtrip");
        using (client)
        {
            var result = await WriteBatchTools.PreviewWriteBatch(client, safety, new[]
            {
                new BatchOperationRequest { OperationId = "type-source", Operation = "update_type_content", ProjectPath = "type-content-roundtrip", TypePath = "PLC_1/Types/AnalogInputSettings", SourceContent = "TYPE AnalogInputSettings STRUCT Value : Int; END_STRUCT END_TYPE" }
            });
            using var doc = JsonDocument.Parse(result);
            var entry = doc.RootElement.GetProperty("diff").GetProperty("operations")[0];
            Assert.Equal("type-source", entry.GetProperty("operationId").GetString());
            Assert.True(entry.GetProperty("current").GetProperty("characterCount").GetInt32() > 0);
            Assert.True(entry.GetProperty("requested").GetProperty("excerpt").GetProperty("lines").GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task PreviewWriteBatch_MixedEligibleAndIneligibleWrites_PreservesEligibleRequestOrder()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "echo");
        using (client)
        {
            var result = await WriteBatchTools.PreviewWriteBatch(client, safety, new[]
            {
                new BatchOperationRequest { OperationId = "block-1", Operation = "update_block_logic", ProjectPath = "echo", BlockPath = "PLC_1/Blocks/Main", YamlContent = "--- FILE: Main.xml\r\n<Document />\r\n" },
                new BatchOperationRequest { OperationId = "tag-1", Operation = "create_tag_table", ProjectPath = "echo", TableName = "Inputs" },
                new BatchOperationRequest { OperationId = "type-3", Operation = "update_type_content", ProjectPath = "echo", TypePath = "PLC_1/Types/AnalogInputSettings", SourceContent = "TYPE AnalogInputSettings STRUCT Value : Int; END_STRUCT END_TYPE" }
            });
            using var doc = JsonDocument.Parse(result);
            var operations = doc.RootElement.GetProperty("diff").GetProperty("operations");
            Assert.Equal(new[] { "block-1", "type-3" }, operations.EnumerateArray().Select(x => x.GetProperty("operationId").GetString()).ToArray());
        }
    }

    [Fact]
    public async Task PreviewWriteBatch_IdenticalContent_ReportsRawAndNormalizedEqualityWithNoChangedExcerpt()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "block-source-roundtrip");
        using (client)
        {
            var result = await WriteBatchTools.PreviewWriteBatch(client, safety, new[]
            {
                new BatchOperationRequest { OperationId = "block-same", Operation = "update_block_logic", ProjectPath = "block-source-roundtrip", BlockPath = "PLC_1/Blocks/Recipe_DB", Format = SourceFormatNames.Source, YamlContent = SourceBlockCurrent }
            });
            using var doc = JsonDocument.Parse(result);
            var entry = doc.RootElement.GetProperty("diff").GetProperty("operations")[0];
            Assert.True(entry.GetProperty("rawTextEqual").GetBoolean());
            Assert.True(entry.GetProperty("normalizedLinesEqual").GetBoolean());
            Assert.False(entry.GetProperty("lineEndingOnly").GetBoolean());
            Assert.Equal(0, entry.GetProperty("currentChangedLineCount").GetInt32());
            Assert.Equal(0, entry.GetProperty("requestedChangedLineCount").GetInt32());
            Assert.Equal(0, entry.GetProperty("current").GetProperty("excerpt").GetProperty("lines").GetArrayLength());
            Assert.Equal(0, entry.GetProperty("requested").GetProperty("excerpt").GetProperty("lines").GetArrayLength());
        }
    }

    [Fact]
    public async Task PreviewWriteBatch_LineEndingOnlyChange_ReportsNormalizedEqualityButNotRawEquality()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "block-source-roundtrip");
        using (client)
        {
            var requested = SourceBlockCurrent.Replace("\r\n", "\n");
            var result = await WriteBatchTools.PreviewWriteBatch(client, safety, new[]
            {
                new BatchOperationRequest { OperationId = "block-eol", Operation = "update_block_logic", ProjectPath = "block-source-roundtrip", BlockPath = "PLC_1/Blocks/Recipe_DB", Format = SourceFormatNames.Source, YamlContent = requested }
            });
            using var doc = JsonDocument.Parse(result);
            var entry = doc.RootElement.GetProperty("diff").GetProperty("operations")[0];
            Assert.False(entry.GetProperty("rawTextEqual").GetBoolean());
            Assert.True(entry.GetProperty("normalizedLinesEqual").GetBoolean());
            Assert.True(entry.GetProperty("lineEndingOnly").GetBoolean());
        }
    }

    [Fact]
    public async Task PreviewWriteBatch_TruncatesOversizedEntriesAndExhaustsTheBatchBudget()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "type-content-roundtrip");
        using (client)
        {
            const int retainedLineLength = 80;
            var oversizedLine = new string('x', retainedLineLength);
            var requested = string.Join("\r\n", Enumerable.Range(1, 60).Select(_ => oversizedLine)) + "\r\n";
            var operations = Enumerable.Range(1, 9).Select(i => new BatchOperationRequest
            {
                OperationId = $"type-{i}", Operation = "update_type_content", ProjectPath = "type-content-roundtrip", TypePath = "PLC_1/Types/AnalogInputSettings", SourceContent = requested
            }).ToArray();
            var result = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
            using var doc = JsonDocument.Parse(result);
            var diffOperations = doc.RootElement.GetProperty("diff").GetProperty("operations").EnumerateArray().ToArray();
            Assert.Equal(40, diffOperations[0].GetProperty("requested").GetProperty("excerpt").GetProperty("lines").GetArrayLength());
            Assert.Equal(retainedLineLength, diffOperations[0].GetProperty("requested").GetProperty("excerpt").GetProperty("lines")[0].GetProperty("text").GetString()!.Length);
            Assert.False(diffOperations[6].GetProperty("batchBudgetExhausted").GetBoolean());
            Assert.True(diffOperations[7].GetProperty("batchBudgetExhausted").GetBoolean());
            Assert.Empty(diffOperations[7].GetProperty("current").GetProperty("excerpt").GetProperty("lines").EnumerateArray());
            Assert.Empty(diffOperations[7].GetProperty("requested").GetProperty("excerpt").GetProperty("lines").EnumerateArray());
            Assert.True(diffOperations[8].GetProperty("batchBudgetExhausted").GetBoolean());
            Assert.Empty(diffOperations[8].GetProperty("current").GetProperty("excerpt").GetProperty("lines").EnumerateArray());
            Assert.Empty(diffOperations[8].GetProperty("requested").GetProperty("excerpt").GetProperty("lines").EnumerateArray());
            Assert.NotEmpty(diffOperations[8].GetProperty("requested").GetProperty("sha256").GetString());
            Assert.True(diffOperations[8].GetProperty("requested").GetProperty("lineCount").GetInt32() > 0);
            Assert.True(diffOperations[8].GetProperty("requested").GetProperty("characterCount").GetInt32() > 0);
        }
    }

    [Fact]
    public async Task PreviewWriteBatch_BatchBudgetExhaustionSuppressesAllLaterEligibleExcerpts()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "type-content-roundtrip");
        using (client)
        {
            var oversized = string.Join("\r\n", Enumerable.Range(1, 60).Select(_ => "x")) + "\r\n";
            var operations = Enumerable.Range(1, 8).Select(i => new BatchOperationRequest
            {
                OperationId = $"oversized-{i}", Operation = "update_type_content", ProjectPath = "type-content-roundtrip", TypePath = "PLC_1/Types/AnalogInputSettings", SourceContent = oversized
            }).Append(new BatchOperationRequest
            {
                OperationId = "small-after-exhaustion", Operation = "update_type_content", ProjectPath = "type-content-roundtrip", TypePath = "PLC_1/Types/AnalogInputSettings", SourceContent = "TYPE AnalogInputSettings STRUCT Value : Int; END_STRUCT END_TYPE"
            }).ToArray();

            var result = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
            using var doc = JsonDocument.Parse(result);
            var diffOperations = doc.RootElement.GetProperty("diff").GetProperty("operations").EnumerateArray().ToArray();
            var exhausted = diffOperations[7];
            var later = diffOperations[8];

            Assert.True(exhausted.GetProperty("batchBudgetExhausted").GetBoolean());
            Assert.True(later.GetProperty("batchBudgetExhausted").GetBoolean());
            Assert.Empty(later.GetProperty("current").GetProperty("excerpt").GetProperty("lines").EnumerateArray());
            Assert.Empty(later.GetProperty("requested").GetProperty("excerpt").GetProperty("lines").EnumerateArray());
        }
    }

    [Fact]
    public async Task PreviewWriteBatch_AllIneligibleWrites_ReturnsNullDiff()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "echo");
        using (client)
        {
            var result = await WriteBatchTools.PreviewWriteBatch(client, safety, new[]
            {
                new BatchOperationRequest { OperationId = "tag-1", Operation = "create_tag_table", ProjectPath = "echo", TableName = "Inputs" }
            });
            using var doc = JsonDocument.Parse(result);
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("diff").ValueKind);
        }
    }
}
