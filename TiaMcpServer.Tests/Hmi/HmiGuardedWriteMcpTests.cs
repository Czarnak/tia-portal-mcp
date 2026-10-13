using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Json;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

[Collection("Mcp protocol serial")]
public class HmiGuardedWriteMcpTests
{
    private const string Scenario = "hmi-write-roundtrip";
    private static object[] Sequence() =>
    [
        new { operationId = "table", operation = "create_tag_table", tableName = "NewTable" },
        new { operationId = "tag", operation = "create_tag", tableName = "NewTable", tagName = "NewTag", dataType = "Bool" },
        new { operationId = "alarm", operation = "create_alarm", alarmName = "NewAlarm", alarmKind = "discrete", triggerTag = "NewTag", eventText = new[] { new { culture = "en-US", text = "A & B" } } },
    ];

    [Theory]
    [InlineData(McpAccessMode.ReadWrite, "none")]
    [InlineData(McpAccessMode.Full, "policy")]
    public async Task PreviewThenApplyDependenciesNormalizeTextsWithoutElicitation(McpAccessMode mode, string confirmation)
    {
        using var audit = new TempAuditDirectory();
        var prompts = 0;
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, audit.Path, Scenario, new McpClientOptions
        {
            Capabilities = new() { Elicitation = new() { Form = new() } },
            Handlers = new() { ElicitationHandler = (_, _) => { prompts++; return ValueTask.FromResult(new ElicitResult { Action = "decline" }); } },
        });
        var preview = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = Sequence(), ["dryRun"] = true }));
        var applied = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = Sequence() }));
        Assert.True(preview.GetProperty("success").GetBoolean(), preview.GetRawText());
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.Equal(preview.GetProperty("effects").GetRawText(), applied.GetProperty("effects").GetRawText());
        Assert.Equal("table", applied.GetProperty("effects")[1].GetProperty("effect").GetProperty("dependsOn").GetString());
        Assert.Contains("&amp;", applied.GetRawText().Replace("\\u0026", "&"));
        Assert.Equal(0, prompts);
        var lines = NetworkGuardedWriteMcpTests.AuditLines(audit.Path);
        Assert.Equal(2, lines.Length);
        Assert.Equal(confirmation, JsonDocument.Parse(lines[1]).RootElement.GetProperty("confirmation").GetProperty("by").GetString());
    }

    [Fact]
    public async Task FailedCreateCarriesCleanupStopsLaterItemsAndKeepsEarlierWrite()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var reply = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new object[]
        {
            Sequence()[0],
            new { operationId = "bad", operation = "create_tag", tableName = "NewTable", tagName = "BadTag", connection = "Connection", plcTag = "Missing" },
            new { operationId = "last", operation = "create_tag_table", tableName = "NeverCreated" },
        } });
        var root = NetworkGuardedWriteMcpTests.Document(reply);
        Assert.False(reply.IsError == true);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal(new[] { "succeeded", "failed", "skipped" }, items.EnumerateArray().Select(i => i.GetProperty("status").GetString()));
        var result = items[1].GetProperty("result");
        Assert.Equal("deleted", result.GetProperty("cleanup").GetString());
        Assert.Equal("PlcTag", result.GetProperty("failedProperty").GetString());
        Assert.Equal(new[] { "Name", "Connection" }, result.GetProperty("appliedProperties").EnumerateArray().Select(p => p.GetString()));
        Assert.True(root.GetProperty("verification").GetProperty("success").GetBoolean(), root.GetRawText());
        var collision = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new[] { Sequence()[0] } }));
        Assert.Equal("blocked", collision.GetProperty("phase").GetString());
        Assert.Equal(2, NetworkGuardedWriteMcpTests.AuditLines(audit.Path).Length);
    }

    [Theory]
    [InlineData("confirm", "true")]
    [InlineData("acknowledge", "[]")]
    [InlineData("unknown", "true")]
    [InlineData("dryRun", "null")]
    [InlineData("dryRun", "\"true\"")]
    public async Task StrictWrapperRejectsWithoutWorkerOrAudit(string key, string json)
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path);
        var before = requests.Methods();
        var result = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = Sequence(), [key] = JsonSerializer.Deserialize<JsonElement>(json) });
        Assert.True(result.IsError);
        Assert.Equal(before, requests.Methods());
        Assert.Empty(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Fact]
    public async Task EscapedMaximumIdentitiesDeniedBeforeBindingWithOneAudit()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path);
        var before = requests.Methods();
        var result = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = Enumerable.Range(0, 50).Select(i => new { operationId = new string('\u0001', 253) + i.ToString("D3"), operation = "create_tag_table", tableName = "T" + i }).ToArray() });
        var root = NetworkGuardedWriteMcpTests.Document(result);
        Assert.True(result.IsError);
        Assert.Equal("validation_error", root.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(before, requests.Methods());
        Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Theory]
    [InlineData("CleanupFails", false)]
    [InlineData("Missing", true)]
    public async Task FailedCreateReadbackDistinguishesCleanupOutcome(string plcTag, bool verified)
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);
        var reply = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new[]
        { new { operationId = "a", operation = "create_tag", tableName = "Existing", tagName = "Bad", connection = "Connection", plcTag } } });
        var root = NetworkGuardedWriteMcpTests.Document(reply);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(verified, root.GetProperty("verification").GetProperty("success").GetBoolean());
        AssertAudit(root, Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
    }

    [Fact]
    public async Task OversizedEffectOmitsDeliveryButPreservesExecutionAndExactAudit()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);
        var reply = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new[]
        { new { operationId = "a", operation = "create_tag", tableName = "Existing", tagName = "Big", dataType = "Bool", comment = new[] { new { culture = "en-US", text = new string('x', 61000) } } } } });
        var root = NetworkGuardedWriteMcpTests.Document(reply);
        Assert.False(reply.IsError == true);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("succeeded", root.GetProperty("batch").GetProperty("operations")[0].GetProperty("status").GetString());
        Assert.True(root.GetProperty("verification").GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("effects")[0].GetProperty("effect").ValueKind);
        Assert.True(CanonicalJson.Serialize(root).Length <= 180000);
        AssertAudit(root, Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
    }

    [Fact]
    public async Task LateChangedInfoMessagesAndBlockerRemainVisibleBeforePartialWriteGuard()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);
        var reply = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new object[]
        {
            Sequence()[0], Sequence()[1],
            new { operationId = "drift", operation = "create_alarm", alarmName = "LateDrift", alarmKind = "discrete", triggerTag = "NewTag" },
            new { operationId = "delete", operation = "delete_tag_table", tableName = "NewTable" },
        } });
        var root = NetworkGuardedWriteMcpTests.Document(reply);
        var guards = root.GetProperty("guards").EnumerateArray().ToArray();
        Assert.Equal(2, guards.Count(g => g.GetProperty("id").GetString() == "hmi_deletes_table_contents"));
        Assert.Equal(2, guards.Count(g => g.GetProperty("id").GetString() == "hmi_deletes_referenced_tag"));
        Assert.Contains(guards, g => g.GetProperty("id").GetString() == "hmi_state_unverifiable");
        Assert.Equal(6, guards.Length);
        Assert.Equal("failed", root.GetProperty("batch").GetProperty("operations")[3].GetProperty("status").GetString());
        AssertAudit(root, Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
    }

    private static void AssertAudit(JsonElement root, string line)
    {
        var audit = JsonDocument.Parse(line).RootElement;
        var text = CanonicalJson.Serialize(root);
        Assert.Equal(text, audit.GetProperty("responseText").GetString());
        Assert.Equal("sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant(), audit.GetProperty("responseHash").GetString());
    }

    [Fact]
    public async Task AllNineOperationsRoundtripWithRenamesAndDeletes()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);
        var result = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = Sequence().Concat(new object[]
        {
            new { operationId = "renameTable", operation = "update_tag_table", tableName = "NewTable", newName = "RenamedTable" },
            new { operationId = "renameTag", operation = "update_tag", tagName = "NewTag", newName = "RenamedTag", acquisitionCycle = "T1s" },
            new { operationId = "renameAlarm", operation = "update_alarm", alarmName = "NewAlarm", alarmKind = "discrete", newName = "RenamedAlarm", priority = 1 },
            new { operationId = "deleteAlarm", operation = "delete_alarm", alarmName = "RenamedAlarm", alarmKind = "discrete" },
            new { operationId = "deleteTag", operation = "delete_tag", tagName = "RenamedTag" },
            new { operationId = "deleteTable", operation = "delete_tag_table", tableName = "RenamedTable" },
        }).ToArray() });
        var root = NetworkGuardedWriteMcpTests.Document(result);
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.All(root.GetProperty("batch").GetProperty("operations").EnumerateArray(), i => Assert.Equal("succeeded", i.GetProperty("status").GetString()));
        AssertAudit(root, Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
    }

    [Fact]
    public async Task ToolSchemaContainsOnlyOperationsAndBooleanDryRun()
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite);
        var tool = Assert.Single(await harness.Client.ListToolsAsync(), t => t.Name == "hmi_write").ProtocolTool;
        var properties = tool.InputSchema.GetProperty("properties");
        Assert.Equal(new[] { "dryRun", "operations" }, properties.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("boolean", properties.GetProperty("dryRun").GetProperty("type").GetString());
        Assert.False(properties.GetProperty("dryRun").GetProperty("default").GetBoolean());
        Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.NotNull(tool.OutputSchema);
    }

    [Fact]
    public async Task ReadOnlyToolIsAbsentAndCannotDispatch()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly, audit.Path);
        Assert.DoesNotContain(await harness.Client.ListToolsAsync(), t => t.Name == "hmi_write");
        await Assert.ThrowsAsync<McpProtocolException>(() => harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = Sequence() }).AsTask());
        Assert.Empty(requests.Methods());
        Assert.Empty(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Fact]
    public async Task FiftyMaximumAsciiIdentitiesAreAdmittedAndPlanned()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);
        var result = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["dryRun"] = true, ["operations"] = Enumerable.Range(0, 50).Select(i => new { operationId = new string('x', 253) + i.ToString("D3"), operation = "create_tag_table", tableName = "Table" + i }).ToArray() });
        var root = NetworkGuardedWriteMcpTests.Document(result);
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal(50, root.GetProperty("effects").GetArrayLength());
        AssertAudit(root, Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
    }
}
