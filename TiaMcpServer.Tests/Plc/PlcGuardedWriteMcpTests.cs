using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Tests.Network;
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

        Assert.True(reply.IsError);
        var text = Assert.Single(reply.Content.OfType<TextContentBlock>()).Text;
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
        var output = tool.OutputSchema!.Value.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Contains("verification", output);
        Assert.Contains("omission", output);
        Assert.Contains("effects", output);
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

        var root = NetworkGuardedWriteMcpTests.Document(reply);
        Assert.False(reply.IsError == true);
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal(dryRun == true ? "preview" : "applied", root.GetProperty("phase").GetString());
        Assert.Equal("1.0", root.GetProperty("contractVersion").GetString());
        Assert.Equal(0, prompts);
        Assert.Equal(dryRun != true, requests.Methods().Contains("create_tag"));
        Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }
}
