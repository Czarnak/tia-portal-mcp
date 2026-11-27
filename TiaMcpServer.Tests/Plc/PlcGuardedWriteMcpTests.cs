using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Json;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

[Collection("Mcp protocol serial")]
public sealed class PlcGuardedWriteMcpTests
{
    private const string Scenario = "plc-write-roundtrip";

    [Theory]
    [InlineData("confirm", "false")]
    [InlineData("acknowledge", "[]")]
    [InlineData("unknown", "true")]
    [InlineData("dryRun", "\"true\"")]
    [InlineData("dryRun", "null")]
    [InlineData("item:yamlContent", "\"<Block/>\"")]
    [InlineData("item:sourceContent", "\"FUNCTION F\"")]
    public async Task LegacyOrMalformedArguments_DoNotReachWorker(string key, string json)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var before = requests.Methods();
        var value = JsonSerializer.Deserialize<JsonElement>(json);
        var item = new Dictionary<string, object?>
        {
            ["operationId"] = "content", ["operation"] = "update_block_logic", ["blockPath"] = "PLC_2/Main",
            ["expectedContentHash"] = "xml:sha256:" + new string('0', 64),
        };
        var args = new Dictionary<string, object?> { ["operations"] = new[] { item } };
        if (key.StartsWith("item:", StringComparison.Ordinal)) item[key["item:".Length..]] = value;
        else args[key] = value;

        var reply = await harness.Client.CallToolAsync("plc_write", args);

        ArgumentRejectionAssert.IsCanonicalValidationError(reply, "plc_write");
        var root = reply.StructuredContent!.Value;
        Assert.Equal("error", root.GetProperty("phase").GetString());
        Assert.Equal("plc_write", root.GetProperty("tool").GetString());
        var text = root.GetProperty("error").GetProperty("message").GetString();
        if (key.StartsWith("item:", StringComparison.Ordinal))
            Assert.Contains("use 'content' with 'expectedContentHash'", text);
        else
            Assert.Contains("dryRun, operations", text);
        Assert.Equal(before, requests.Methods());
        Assert.Empty(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Fact]
    public async Task RegisteredSchema_OnlyOperationsAndBooleanDryRun()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path);
        var tool = Assert.Single(await harness.Client.ListToolsAsync(), t => t.Name == "plc_write").ProtocolTool;
        var properties = tool.InputSchema.GetProperty("properties");

        Assert.Equal(new[] { "dryRun", "operations" }, properties.EnumerateObject().Select(p => p.Name).Order());
        Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("boolean", properties.GetProperty("dryRun").GetProperty("type").GetString());
        Assert.False(properties.GetProperty("dryRun").GetProperty("default").GetBoolean());
        var item = properties.GetProperty("operations").GetProperty("items").GetRawText();
        Assert.Contains("\"expectedContentHash\"", item);
        Assert.DoesNotContain("yamlContent", item);
        Assert.DoesNotContain("sourceContent", item);
        Assert.DoesNotContain("isSafety", item);
        var output = tool.OutputSchema!.Value.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Contains("verification", output);
        Assert.Contains("omission", output);
        Assert.Contains("effects", output);
    }

    [Fact]
    public async Task PatchSchema_RequiresClosedNonNullableEditStrings()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path);
        var tool = Assert.Single(await harness.Client.ListToolsAsync(), t => t.Name == "plc_write").ProtocolTool;
        var operation = tool.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items");
        if (operation.TryGetProperty("required", out var required))
            Assert.DoesNotContain("edits", required.EnumerateArray().Select(v => v.GetString()));
        var edits = operation.GetProperty("properties").GetProperty("edits");
        Assert.Equal("array", edits.GetProperty("type").GetString());
        var edit = edits.GetProperty("items");
        Assert.Equal("object", edit.GetProperty("type").GetString());
        Assert.False(edit.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "newText", "oldText" }, edit.GetProperty("required").EnumerateArray().Select(v => v.GetString()).Order());
        foreach (var name in new[] { "oldText", "newText" })
            Assert.Equal("string", edit.GetProperty("properties").GetProperty(name).GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("\"edits\"")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[{\"oldText\":\"old\"}]")]
    [InlineData("[{\"newText\":\"new\"}]")]
    [InlineData("[{\"oldText\":null,\"newText\":\"new\"}]")]
    [InlineData("[{\"oldText\":\"old\",\"newText\":null}]")]
    [InlineData("[{\"oldText\":12,\"newText\":\"new\"}]")]
    [InlineData("[{\"oldText\":\"old\",\"newText\":false}]")]
    [InlineData("[{\"oldText\":\"old\",\"newText\":\"new\",\"unknown\":true}]")]
    public async Task MalformedPatchEdits_AreCanonicalUnauditedShapeErrors(string editsJson)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var before = requests.Methods();
        var patch = new Dictionary<string, object?>
        {
            ["operationId"] = "patch", ["operation"] = "patch_block_logic", ["blockPath"] = "PLC_2/Main",
            ["expectedContentHash"] = "xml:sha256:" + new string('0', 64),
            ["edits"] = JsonSerializer.Deserialize<JsonElement>(editsJson),
        };

        var reply = await harness.Client.CallToolAsync("plc_write", new Dictionary<string, object?> { ["operations"] = new[] { patch } });

        ArgumentRejectionAssert.IsCanonicalValidationError(reply, "plc_write");
        Assert.Equal(before, requests.Methods());
        Assert.Empty(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    private static async Task<(string Content, string Hash)> ReadBlockAsync(McpClient client)
    {
        var result = await client.CallToolAsync("plc_read", new Dictionary<string, object?>
        {
            ["operations"] = new[] { new { operationId = "read", operation = "get_block_content", blockPath = "PLC_2/Main" } },
        });
        var root = CanonicalToolDocument.Read(result);
        var content = Assert.Single(root.GetProperty("batch").GetProperty("operations").EnumerateArray()).GetProperty("result");
        return (content.GetProperty("content").GetString()!, content.GetProperty("contentHash").GetString()!);
    }

    [Fact]
    public async Task RegisteredPatch_PreviewsThenAppliesWithPublicNamesAndFreshVerification()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        var prompts = 0;
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario,
            new McpClientOptions
            {
                Capabilities = new() { Elicitation = new() { Form = new() } },
                Handlers = new() { ElicitationHandler = (_, _) => { prompts++; return ValueTask.FromResult(new ElicitResult { Action = "decline" }); } },
            });
        var original = await ReadBlockAsync(harness.Client);
        var patch = new { operationId = "patch", operation = "patch_block_logic", blockPath = "PLC_2/Main",
            expectedContentHash = original.Hash, edits = new[] { new { oldText = "format=\"xml\"", newText = "format=\"patched\"" } } };
        var args = new Dictionary<string, object?> { ["operations"] = new[] { patch }, ["dryRun"] = true };

        var preview = await harness.Client.CallToolAsync("plc_write", args);

        var previewRoot = CanonicalToolDocument.Read(preview);
        Assert.False(preview.IsError == true);
        Assert.True(previewRoot.GetProperty("success").GetBoolean(), previewRoot.GetRawText());
        Assert.Equal("2.1", previewRoot.GetProperty("contractVersion").GetString());
        Assert.Equal("preview", previewRoot.GetProperty("phase").GetString());
        var effect = Assert.Single(previewRoot.GetProperty("effects").EnumerateArray()).GetProperty("effect");
        Assert.Equal("patch_block_logic", effect.GetProperty("operation").GetString());
        Assert.Equal(1, effect.GetProperty("contentDiff").GetProperty("currentChangedLineCount").GetInt32());
        Assert.Equal(1, effect.GetProperty("contentDiff").GetProperty("requestedChangedLineCount").GetInt32());
        Assert.DoesNotContain("update_block_logic", requests.Methods());
        using (var previewAudit = JsonDocument.Parse(Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path))))
            Assert.All(previewAudit.RootElement.GetProperty("items").EnumerateArray(), item => Assert.False(item.TryGetProperty("resolvedOperation", out _)));

        args["dryRun"] = false;
        var applied = await harness.Client.CallToolAsync("plc_write", args);

        var root = CanonicalToolDocument.Read(applied);
        Assert.False(applied.IsError == true);
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        var item = Assert.Single(root.GetProperty("batch").GetProperty("operations").EnumerateArray());
        Assert.Equal("patch_block_logic", item.GetProperty("operation").GetString());
        Assert.Equal("succeeded", item.GetProperty("status").GetString());
        Assert.Equal(1, requests.Methods().Count(method => method == "update_block_logic"));
        Assert.DoesNotContain("patch_block_logic", requests.Methods());
        var fresh = await ReadBlockAsync(harness.Client);
        Assert.Equal(original.Content.Replace("format=\"xml\"", "format=\"patched\""), fresh.Content);
        var verification = Assert.Single(root.GetProperty("verification").GetProperty("operations").EnumerateArray());
        Assert.Equal("contentHash", verification.GetProperty("check").GetString());
        Assert.Equal(fresh.Hash, verification.GetProperty("actual").GetString());
        Assert.Equal(fresh.Hash, verification.GetProperty("contentHash").GetString());
        var readBack = verification.GetProperty("readBack");
        Assert.Equal("xmlDocument", readBack.GetProperty("comparisonScope").GetString());
        Assert.True(readBack.GetProperty("rawTextEqual").GetBoolean());
        Assert.True(readBack.GetProperty("normalizedLinesEqual").GetBoolean());
        Assert.Empty(readBack.GetProperty("submitted").GetProperty("excerpt").GetProperty("lines").EnumerateArray());
        Assert.Empty(readBack.GetProperty("observed").GetProperty("excerpt").GetProperty("lines").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, readBack.GetProperty("omission").ValueKind);
        Assert.Equal(0, prompts);
        Assert.Equal(2, NetworkGuardedWriteMcpTests.AuditLines(audit.Path).Length);
        var record = AssertCanonicalAudit(applied, NetworkGuardedWriteMcpTests.AuditLines(audit.Path).Last());
        var requested = Assert.Single(record.GetProperty("requestedOperations").EnumerateArray());
        Assert.Equal("patch_block_logic", requested.GetProperty("operation").GetString());
        Assert.Equal("format=\"xml\"", requested.GetProperty("edits")[0].GetProperty("oldText").GetString());
        var attempted = Assert.Single(record.GetProperty("items").EnumerateArray()).GetProperty("resolvedOperation");
        Assert.Equal("update_block_logic", attempted.GetProperty("operation").GetString());
        Assert.Equal(fresh.Content, attempted.GetProperty("content").GetString());
        Assert.Equal(original.Hash, attempted.GetProperty("expectedContentHash").GetString());
        Assert.False(attempted.TryGetProperty("edits", out _));
    }

    [Fact]
    public async Task ReadBackOmissionMatchesCanonicalAuditAndRetainsAttempt()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite,
            audit.Path, "plc-write-readback-normalizes");
        var original = await ReadBlockAsync(harness.Client);
        // JSON expansion exercises the encoded delivery limit in this Siemens-free fixture.
        // Differences at both ends keep the intervening lines inside the observed changed span.
        var submitted = "<Document><ConstantType>LReal</ConstantType>\n"
            + string.Concat(Enumerable.Repeat("<!--" + new string('\u0001', 400) + "-->\n", 18))
            + "<ConstantType>LReal</ConstantType></Document>";
        var patch = new
        {
            operationId = "budgeted-patch", operation = "patch_block_logic", blockPath = "PLC_2/Main",
            expectedContentHash = original.Hash,
            edits = new[] { new { oldText = original.Content, newText = submitted } },
        };

        var reply = await harness.Client.CallToolAsync("plc_write", new Dictionary<string, object?>
        {
            ["operations"] = new[] { patch },
        });

        var root = CanonicalToolDocument.Read(reply);
        Assert.False(reply.IsError == true);
        Assert.Equal("2.1", root.GetProperty("contractVersion").GetString());
        Assert.False(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("succeeded", Assert.Single(root.GetProperty("batch").GetProperty("operations").EnumerateArray())
            .GetProperty("status").GetString());
        Assert.True(root.GetProperty("verification").GetProperty("success").GetBoolean());
        var check = Assert.Single(root.GetProperty("verification").GetProperty("operations").EnumerateArray());
        Assert.True(check.GetProperty("success").GetBoolean());
        var readBack = check.GetProperty("readBack");
        Assert.False(readBack.GetProperty("rawTextEqual").GetBoolean());
        Assert.Equal(JsonValueKind.Object, readBack.GetProperty("omission").ValueKind);
        Assert.Empty(readBack.GetProperty("submitted").GetProperty("excerpt").GetProperty("lines").EnumerateArray());
        Assert.Empty(readBack.GetProperty("observed").GetProperty("excerpt").GetProperty("lines").EnumerateArray());
        var record = AssertCanonicalAudit(reply, Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
        var requested = Assert.Single(record.GetProperty("requestedOperations").EnumerateArray());
        Assert.Equal("patch_block_logic", requested.GetProperty("operation").GetString());
        Assert.Equal(submitted, requested.GetProperty("edits")[0].GetProperty("newText").GetString());
        var item = Assert.Single(record.GetProperty("items").EnumerateArray());
        Assert.Equal("succeeded", item.GetProperty("status").GetString());
        var attempted = item.GetProperty("resolvedOperation");
        Assert.Equal("update_block_logic", attempted.GetProperty("operation").GetString());
        var attemptedContent = attempted.GetProperty("content").GetString()!;
        // The original single-line export has no terminator, so patch resolution uses CRLF.
        Assert.Equal(submitted.Replace("\n", "\r\n", StringComparison.Ordinal), attemptedContent);
        Assert.Equal(original.Hash, attempted.GetProperty("expectedContentHash").GetString());
        Assert.False(attempted.TryGetProperty("edits", out _));
        var fresh = await ReadBlockAsync(harness.Client);
        Assert.Equal(attemptedContent.Replace("<ConstantType>LReal</ConstantType>", "<ConstantType>Real</ConstantType>", StringComparison.Ordinal), fresh.Content);
        Assert.Equal(fresh.Hash, check.GetProperty("actual").GetString());
        Assert.Equal(fresh.Hash, check.GetProperty("contentHash").GetString());
    }

    private static JsonElement AssertCanonicalAudit(CallToolResult reply, string auditLine)
    {
        var text = Assert.Single(reply.Content.OfType<TextContentBlock>()).Text;
        Assert.InRange(text.Length, 1, 180_000);
        Assert.Equal(text, CanonicalJson.Serialize(reply.StructuredContent!.Value));
        using var record = JsonDocument.Parse(auditLine);
        Assert.Equal(text, record.RootElement.GetProperty("responseText").GetString());
        Assert.Equal("sha256:" + ContentHashRules.Sha256Hex(text), record.RootElement.GetProperty("responseHash").GetString());
        return record.RootElement.Clone();
    }

    [Fact]
    public async Task RegisteredPatch_EmptyReplacementPassesShapeAndPreviewsWholeScopeDeletion()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var original = await ReadBlockAsync(harness.Client);
        var reply = await harness.Client.CallToolAsync("plc_write", new Dictionary<string, object?>
        {
            ["dryRun"] = true,
            ["operations"] = new[] { new { operationId = "delete-text", operation = "patch_block_logic", blockPath = "PLC_2/Main",
                expectedContentHash = original.Hash, edits = new[] { new { oldText = original.Content, newText = "" } } } },
        });

        var root = CanonicalToolDocument.Read(reply);
        Assert.False(reply.IsError == true);
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("preview", root.GetProperty("phase").GetString());
    }

    [Fact]
    public async Task RegisteredMixedBatch_InvalidPatchAnchorRejectsWholePlanBeforeEarlierCreate()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var original = await ReadBlockAsync(harness.Client);
        var reply = await harness.Client.CallToolAsync("plc_write", new Dictionary<string, object?>
        {
            ["operations"] = new object[] { CreateTag("early", "NeverCreated"),
                new { operationId = "invalid", operation = "patch_block_logic", blockPath = "PLC_2/Main", expectedContentHash = original.Hash,
                    edits = new[] { new { oldText = "missing anchor", newText = "new" } } } },
        });

        var root = CanonicalToolDocument.Read(reply);
        Assert.True(reply.IsError == true);
        Assert.Equal("validation_error", root.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("edits[0]", root.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain("create_tag", requests.Methods());
        Assert.DoesNotContain("update_block_logic", requests.Methods());
    }

    [Theory]
    [InlineData("true", false)]
    [InlineData("false", false)]
    [InlineData("null", false)]
    [InlineData("true", true)]
    public async Task UpdateTagIsSafety_RejectedBeforeWorker(string json, bool dryRun)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var before = requests.Methods();
        var item = new Dictionary<string, object?>
        {
            ["operationId"] = "tag", ["operation"] = "update_tag", ["plcName"] = "PLC_2",
            ["tableName"] = "Default tag table", ["name"] = "Start",
            ["isSafety"] = JsonSerializer.Deserialize<JsonElement>(json),
        };

        var reply = await harness.Client.CallToolAsync("plc_write", new Dictionary<string, object?> { ["operations"] = new[] { item }, ["dryRun"] = dryRun });

        ArgumentRejectionAssert.IsCanonicalValidationError(reply, "plc_write");
        Assert.Equal(before, requests.Methods());
        Assert.Empty(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Fact]
    public async Task PlcReadItemSchema_HasNoIsSafety()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path);
        var tool = Assert.Single(await harness.Client.ListToolsAsync(), t => t.Name == "plc_read").ProtocolTool;

        var item = tool.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetRawText();

        Assert.DoesNotContain("isSafety", item);
    }

    [Fact]
    public async Task PlcReadItemWithIsSafety_RejectedBeforeWorker()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var before = requests.Methods();
        var item = new Dictionary<string, object?>
        {
            ["operationId"] = "r", ["operation"] = "get_block_content", ["blockPath"] = "PLC_2/Main", ["isSafety"] = true,
        };

        var reply = await harness.Client.CallToolAsync("plc_read", new Dictionary<string, object?> { ["operations"] = new[] { item } });

        ArgumentRejectionAssert.IsCanonicalValidationError(reply, "plc_read");
        Assert.Equal(before, requests.Methods());
        Assert.Empty(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Fact]
    public async Task UpdateTag_WithoutIsSafety_StillApplies()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var args = new Dictionary<string, object?>
        {
            ["operations"] = new[] { new { operationId = "tag", operation = "update_tag", plcName = "PLC_2", tableName = "Default tag table", name = "Start", externalWritable = false } },
        };

        var reply = await harness.Client.CallToolAsync("plc_write", args);

        var root = CanonicalToolDocument.Read(reply);
        Assert.False(reply.IsError == true);
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("applied", root.GetProperty("phase").GetString());
        Assert.Contains("update_tag", requests.Methods());
        Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    // Captured live (Task 6, L1/L10: TIA Portal V21, English UI); the FakeWorker replays it verbatim.
    private const string LiveTagCollisionMessage = "Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagComposition'."
        + "\r\n\r\nAn attempt was made to create an object that already exists.";

    private static object CreateTag(string id, string name)
        => new { operationId = id, operation = "create_tag", plcName = "PLC_2", tableName = "Default tag table", name, dataType = "Bool" };

    private static void AssertLiveTagCollision(JsonElement item)
    {
        Assert.Equal("failed", item.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("result").ValueKind);
        var failure = item.GetProperty("failure");
        Assert.Equal("engineering_rejected", failure.GetProperty("category").GetString());
        Assert.Equal("TIA Portal rejected the operation: " + LiveTagCollisionMessage, failure.GetProperty("message").GetString());
        var error = failure.GetProperty("engineeringError");
        Assert.Equal(new[] { "inner", "message", "type" }, error.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("EngineeringTargetInvocationException", error.GetProperty("type").GetString());
        Assert.Equal(LiveTagCollisionMessage, error.GetProperty("message").GetString());
        Assert.Empty(error.GetProperty("inner").EnumerateArray());
    }

    [Fact]
    public async Task LiveL1_CreateTagWithExistingName_IsEngineeringRejectedWithCapturedDocument()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);

        var reply = await harness.Client.CallToolAsync("plc_write",
            new Dictionary<string, object?> { ["operations"] = new[] { CreateTag("l1-01", "Start") } });

        var root = CanonicalToolDocument.Read(reply);
        Assert.False(reply.IsError == true);
        Assert.Equal("applied", root.GetProperty("phase").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var item = Assert.Single(root.GetProperty("batch").GetProperty("operations").EnumerateArray());
        AssertLiveTagCollision(item);
        Assert.Empty(root.GetProperty("verification").GetProperty("operations").EnumerateArray());
        Assert.True(root.GetProperty("verification").GetProperty("success").GetBoolean());
        Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Fact]
    public async Task LiveL10_SecondItemCollides_FirstAppliedSecondRejectedThirdSkipped()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);

        var reply = await harness.Client.CallToolAsync("plc_write", new Dictionary<string, object?>
        {
            ["operations"] = new[] { CreateTag("l10-01", "L10Tag1"), CreateTag("l10-02", "Start"), CreateTag("l10-03", "L10Tag3") },
        });

        var root = CanonicalToolDocument.Read(reply);
        Assert.False(reply.IsError == true);
        Assert.Equal("applied", root.GetProperty("phase").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var batch = root.GetProperty("batch");
        var counts = batch.GetProperty("counts");
        Assert.Equal((1, 1, 1, 0), (counts.GetProperty("succeeded").GetInt32(), counts.GetProperty("failed").GetInt32(),
            counts.GetProperty("skipped").GetInt32(), counts.GetProperty("omitted").GetInt32()));
        var items = batch.GetProperty("operations").EnumerateArray().ToArray();
        Assert.Equal("succeeded", items[0].GetProperty("status").GetString());
        Assert.Equal("L10Tag1", items[0].GetProperty("result").GetProperty("tagName").GetString());
        AssertLiveTagCollision(items[1]);
        Assert.Equal("skipped", items[2].GetProperty("status").GetString());
        Assert.Equal("earlierOperationFailed", items[2].GetProperty("skipReason").GetString());
        var guard = Assert.Single(root.GetProperty("guards").EnumerateArray());
        Assert.Equal(("partial_write_no_rollback", "info", "l10-02"), (guard.GetProperty("id").GetString(),
            guard.GetProperty("severity").GetString(), guard.GetProperty("operationId").GetString()));
        var check = Assert.Single(root.GetProperty("verification").GetProperty("operations").EnumerateArray());
        Assert.Equal(("l10-01", "trusted"), (check.GetProperty("operationId").GetString(), check.GetProperty("check").GetString()));
        Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Fact]
    public async Task ObEventClassSchemaEnumListsClassesAndNull()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path);
        var tool = Assert.Single(await harness.Client.ListToolsAsync(), t => t.Name == "plc_write").ProtocolTool;

        var items = tool.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items");
        var values = items.GetProperty("properties").GetProperty("obEventClass").GetProperty("enum").EnumerateArray()
            .Select(v => v.GetString()).ToArray();

        Assert.Equal(ObEventClasses.All.Select(c => c.Name).Cast<string?>().Append(null), values);
    }

    [Theory]
    [InlineData(McpAccessMode.ReadWrite, null)]
    [InlineData(McpAccessMode.ReadWrite, false)]
    [InlineData(McpAccessMode.ReadWrite, true)]
    [InlineData(McpAccessMode.Full, null)]
    [InlineData(McpAccessMode.Full, false)]
    [InlineData(McpAccessMode.Full, true)]
    public async Task PlcWriteNeverElicits(McpAccessMode mode, bool? dryRun)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        var prompts = 0;
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, audit.Path, Scenario, new McpClientOptions
        {
            Capabilities = new() { Elicitation = new() { Form = new() } },
            Handlers = new() { ElicitationHandler = (_, _) => { prompts++; return ValueTask.FromResult(new ElicitResult { Action = "decline" }); } },
        });
        var args = new Dictionary<string, object?>
        {
            ["operations"] = new[] { new { operationId = "tag", operation = "create_tag", plcName = "PLC_2", tableName = "Default tag table", name = "Fresh", dataType = "Bool" } },
        };
        if (dryRun.HasValue) args["dryRun"] = dryRun.Value;

        var reply = await harness.Client.CallToolAsync("plc_write", args);

        var root = CanonicalToolDocument.Read(reply);
        Assert.False(reply.IsError == true);
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal(dryRun == true ? "preview" : "applied", root.GetProperty("phase").GetString());
        Assert.Equal("2.1", root.GetProperty("contractVersion").GetString());
        Assert.Equal(0, prompts);
        Assert.Equal(dryRun != true, requests.Methods().Contains("create_tag"));
        Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }
}
