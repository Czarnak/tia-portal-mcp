using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

[Collection("Mcp protocol serial")]
public sealed class PlcSharedCompilationPipelineTests
{
    private const string Scenario = "plc-write-roundtrip";
    private const string FirstPath = "Station_1/Main";
    private const string SecondPath = "Station_1/Blocks/Motors/FB_Motor";

    [Fact]
    public async Task TwoSourceUpdates_OneCompilePerSelector_AfterAllImports()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var first = await UpdateAsync(harness.Client, "first", FirstPath);
        var second = await UpdateAsync(harness.Client, "second", SecondPath);

        var root = CanonicalToolDocument.Read(await WriteAsync(harness.Client, false, first, second));

        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("shared-per-plc", root.GetProperty("compilationMode").GetString());
        var compilation = Assert.Single(Compilations(root));
        Assert.Equal("Station_1", compilation.GetProperty("plcName").GetString());
        Assert.Equal(new[] { "first", "second" }, compilation.GetProperty("operationIds").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal("succeeded", compilation.GetProperty("stage").GetString());
        Assert.All(Operations(root), item => Assert.Equal("deferred", item.GetProperty("result").GetProperty("importOutcome").GetProperty("compileStage").GetString()));
        var methods = requests.Methods();
        var compile = Assert.Single(methods.Select((method, index) => (method, index)).Where(v => v.method == "compile_check")).index;
        Assert.True(Array.FindLastIndex(methods, method => method == "update_block_logic") < compile);
        Assert.Equal(2, methods.Skip(compile + 1).Count(method => method == "get_block_content"));
    }

    [Fact]
    public async Task CrossPlcBatch_CompilesEachSelectorOnce()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var first = await UpdateAsync(harness.Client, "first", FirstPath);
        var second = await UpdateAsync(harness.Client, "second", "PLC_2/Main");

        var root = CanonicalToolDocument.Read(await WriteAsync(harness.Client, false, first, second));

        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal(new[] { "Station_1", "PLC_2" }, Compilations(root).Select(entry => entry.GetProperty("plcName").GetString()));
        Assert.All(Compilations(root), entry => Assert.Single(entry.GetProperty("report").GetProperty("plcs").EnumerateArray()));
        Assert.Equal(2, requests.Methods().Count(method => method == "compile_check"));
    }

    [Fact]
    public async Task PatchPlusUpdate_SourceFormat_SharedMode()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var original = await ReadAsync(harness.Client, FirstPath);
        var patch = new Dictionary<string, object?>
        {
            ["operationId"] = "patch", ["operation"] = "patch_block_logic", ["blockPath"] = FirstPath,
            ["format"] = "source", ["expectedContentHash"] = original.Hash,
            ["edits"] = new[] { new { oldText = original.Content, newText = original.Content + "\n// patched" } },
        };
        var update = await UpdateAsync(harness.Client, "update", SecondPath);

        var root = CanonicalToolDocument.Read(await WriteAsync(harness.Client, false, patch, update));

        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("shared-per-plc", root.GetProperty("compilationMode").GetString());
        Assert.Equal("patch_block_logic", Operations(root)[0].GetProperty("operation").GetString());
        Assert.Equal(new[] { "patch", "update" }, Assert.Single(Compilations(root)).GetProperty("operationIds").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(original.Content + "\r\n// patched", (await ReadAsync(harness.Client, FirstPath)).Content);
    }

    [Fact]
    public async Task SingleSourceUpdate_PerOperation_NoCompileCheckCall()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var update = await UpdateAsync(harness.Client, "single", FirstPath);

        var root = CanonicalToolDocument.Read(await WriteAsync(harness.Client, false, update));

        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("per-operation", root.GetProperty("compilationMode").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("sharedCompilations").ValueKind);
        Assert.Equal("succeeded", Assert.Single(Operations(root)).GetProperty("result").GetProperty("importOutcome").GetProperty("compileStage").GetString());
        Assert.DoesNotContain("compile_check", requests.Methods());
    }

    [Fact]
    public async Task MixedCall_TagEditAndXmlImport_UnchangedWhileSourceDeferred()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var first = await UpdateAsync(harness.Client, "first", FirstPath);
        var second = await UpdateAsync(harness.Client, "second", SecondPath);
        var xml = await UpdateAsync(harness.Client, "xml", "PLC_2/Main", format: "xml");
        var tag = new Dictionary<string, object?>
        {
            ["operationId"] = "tag", ["operation"] = "update_tag", ["plcName"] = "PLC_2",
            ["tableName"] = "Default tag table", ["name"] = "Start", ["newName"] = "StartRenamed",
        };

        var root = CanonicalToolDocument.Read(await WriteAsync(harness.Client, false, first, tag, xml, second));

        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal(new[] { "first", "second" }, Assert.Single(Compilations(root)).GetProperty("operationIds").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal("succeeded", Operations(root)[2].GetProperty("result").GetProperty("importOutcome").GetProperty("compileStage").GetString());
        Assert.Equal("trusted", root.GetProperty("verification").GetProperty("operations")[1].GetProperty("check").GetString());
    }

    [Theory]
    [InlineData("// fixture: shared-compile-errors", "failed", null)]
    [InlineData("// fixture: shared-compile-worker-failure", "unavailable", "worker_operation_failed")]
    [InlineData("// fixture: shared-compile-malformed", "unavailable", "protocol_error")]
    public async Task CompileErrors_RootSuccessStaysTrue(string marker, string stage, string? failureCategory)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var first = await UpdateAsync(harness.Client, "first", FirstPath, marker);
        var second = await UpdateAsync(harness.Client, "second", SecondPath);

        var root = CanonicalToolDocument.Read(await WriteAsync(harness.Client, false, first, second));

        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.True(root.GetProperty("verification").GetProperty("success").GetBoolean());
        Assert.All(Operations(root), item => Assert.Equal("succeeded", item.GetProperty("status").GetString()));
        var compilation = Assert.Single(Compilations(root));
        Assert.Equal(stage, compilation.GetProperty("stage").GetString());
        if (failureCategory is null)
            Assert.True(compilation.GetProperty("report").GetProperty("totalErrorCount").GetInt32() > 0);
        else
            Assert.Equal(failureCategory, compilation.GetProperty("failure").GetProperty("category").GetString());
        Assert.DoesNotContain("untrustedCompilePayload", root.GetRawText());
    }

    [Fact]
    public async Task GenerationFailureAtItem2_CompilesItem1Only_ReadbackKeepsHashes()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var first = await UpdateAsync(harness.Client, "first", FirstPath);
        var second = await UpdateAsync(harness.Client, "second", SecondPath, "// fixture: shared-import-generation-failure");
        var third = await UpdateAsync(harness.Client, "third", "PLC_2/Main");

        var root = CanonicalToolDocument.Read(await WriteAsync(harness.Client, false, first, second, third));

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(new[] { "succeeded", "failed", "skipped" }, Operations(root).Select(item => item.GetProperty("status").GetString()));
        Assert.Equal(new[] { "first" }, Assert.Single(Compilations(root)).GetProperty("operationIds").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(1, requests.Methods().Count(method => method == "compile_check"));
        var check = root.GetProperty("verification").GetProperty("operations").EnumerateArray().Single(item => item.GetProperty("operationId").GetString() == "first");
        Assert.Equal((await ReadAsync(harness.Client, FirstPath)).Hash, check.GetProperty("contentHash").GetString());
    }

    [Fact]
    public async Task CommittedThenFailedItem_IsIncludedInSharedCompilation()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var first = await UpdateAsync(harness.Client, "first", FirstPath);
        var second = await UpdateAsync(harness.Client, "second", SecondPath, "// fixture: shared-import-committed-failure");

        var root = CanonicalToolDocument.Read(await WriteAsync(harness.Client, false, first, second));

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(new[] { "first", "second" }, Assert.Single(Compilations(root)).GetProperty("operationIds").EnumerateArray().Select(v => v.GetString()));
        Assert.True(Operations(root)[1].GetProperty("failure").GetProperty("blockImportOutcome").GetProperty("targetMutationCommitted").GetBoolean());
    }

    [Fact]
    public async Task UncertainStop_SkipsCompile_ReadbackUnchanged()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var first = await UpdateAsync(harness.Client, "first", FirstPath);
        var second = await UpdateAsync(harness.Client, "second", SecondPath, "// fixture: shared-import-protocol-error");

        var root = CanonicalToolDocument.Read(await WriteAsync(harness.Client, false, first, second));

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("protocol_error", Operations(root)[1].GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal("skipped", Assert.Single(Compilations(root)).GetProperty("stage").GetString());
        Assert.DoesNotContain("compile_check", requests.Methods());
        var check = root.GetProperty("verification").GetProperty("operations").EnumerateArray().Single(item => item.GetProperty("operationId").GetString() == "first");
        Assert.Equal((await ReadAsync(harness.Client, FirstPath)).Hash, check.GetProperty("contentHash").GetString());
    }

    [Fact]
    public async Task DryRun_ReportsSharedModeAndNullCompilations()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var first = await UpdateAsync(harness.Client, "first", FirstPath);
        var second = await UpdateAsync(harness.Client, "second", SecondPath);

        var root = CanonicalToolDocument.Read(await WriteAsync(harness.Client, true, first, second));

        Assert.Equal("preview", root.GetProperty("phase").GetString());
        Assert.Equal("shared-per-plc", root.GetProperty("compilationMode").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("sharedCompilations").ValueKind);
        Assert.DoesNotContain("update_block_logic", requests.Methods());
        Assert.DoesNotContain("compile_check", requests.Methods());
    }

    [Fact]
    public async Task ValidationRejection_ReportsMode()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var first = await UpdateAsync(harness.Client, "first", FirstPath);
        var second = await UpdateAsync(harness.Client, "second", SecondPath);
        second["content"] = null;
        var before = requests.Methods();

        var reply = await WriteAsync(harness.Client, false, first, second);
        var root = CanonicalToolDocument.Read(reply);

        Assert.True(reply.IsError);
        Assert.Equal("validation_error", root.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal("shared-per-plc", root.GetProperty("compilationMode").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("sharedCompilations").ValueKind);
        Assert.Equal(before, requests.Methods());
    }

    [Fact]
    public async Task ContentHashAfterSharedCompile_EqualsNextPlcReadHash()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var first = await UpdateAsync(harness.Client, "first", FirstPath);
        var second = await UpdateAsync(harness.Client, "second", SecondPath);

        var root = CanonicalToolDocument.Read(await WriteAsync(harness.Client, false, first, second));

        var checks = root.GetProperty("verification").GetProperty("operations").EnumerateArray().ToArray();
        Assert.Equal((await ReadAsync(harness.Client, FirstPath)).Hash, checks[0].GetProperty("contentHash").GetString());
        Assert.Equal((await ReadAsync(harness.Client, SecondPath)).Hash, checks[1].GetProperty("contentHash").GetString());
    }

    [Fact]
    public async Task AuditRecord_ContainsSharedCompilations()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var first = await UpdateAsync(harness.Client, "first", FirstPath);
        var second = await UpdateAsync(harness.Client, "second", SecondPath);

        var root = CanonicalToolDocument.Read(await WriteAsync(harness.Client, false, first, second));

        using var record = JsonDocument.Parse(Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
        using var delivered = JsonDocument.Parse(record.RootElement.GetProperty("responseText").GetString()!);
        Assert.Equal(root.GetRawText(), delivered.RootElement.GetRawText());
        Assert.Single(Compilations(delivered.RootElement));
    }

    private static async Task<Dictionary<string, object?>> UpdateAsync(McpClient client, string id, string path,
        string? suffix = null, string format = "source")
    {
        var original = await ReadAsync(client, path, format);
        return new()
        {
            ["operationId"] = id, ["operation"] = "update_block_logic", ["blockPath"] = path,
            ["format"] = format, ["expectedContentHash"] = original.Hash,
            ["content"] = original.Content + "\n// " + id + "\n" + suffix,
        };
    }

    private static async Task<(string Content, string Hash)> ReadAsync(McpClient client, string path, string format = "source")
    {
        var reply = await client.CallToolAsync("plc_read", new Dictionary<string, object?>
        {
            ["operations"] = new[] { new { operationId = "read", operation = "get_block_content", blockPath = path, format } },
        });
        var root = CanonicalToolDocument.Read(reply);
        var result = Assert.Single(Operations(root)).GetProperty("result");
        return (result.GetProperty("content").GetString()!, result.GetProperty("contentHash").GetString()!);
    }

    private static ValueTask<CallToolResult> WriteAsync(McpClient client, bool dryRun, params Dictionary<string, object?>[] operations)
        => client.CallToolAsync("plc_write", new Dictionary<string, object?> { ["operations"] = operations, ["dryRun"] = dryRun });

    private static JsonElement[] Operations(JsonElement root) => root.GetProperty("batch").GetProperty("operations").EnumerateArray().ToArray();
    private static JsonElement[] Compilations(JsonElement root) => root.GetProperty("sharedCompilations").EnumerateArray().ToArray();
}
