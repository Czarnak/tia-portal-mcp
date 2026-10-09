using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Json;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Network;

[Collection("Mcp protocol serial")]
public sealed class NetworkGuardedWriteMcpTests
{
    [Fact]
    public async Task QualifiedSelector_RoundTripsRegisteredSchema()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path);
        var tools = await harness.Client.ListToolsAsync();
        foreach (var name in new[] { "network_read", "network_write" })
        {
            var schema = Assert.Single(tools, t => t.Name == name).ProtocolTool.InputSchema;
            Assert.Contains("\"interfacePath\"", schema.GetRawText());
            Assert.Contains("\"positionNumber\"", schema.GetRawText());
        }
        var selector = new NetworkObjectSelectorInfo
        {
            Kind = "node", DeviceName = "PLC_1", NodeId = "E1", InterfaceName = "X1",
            InterfacePath = new() { new() { Name = "X1", PositionNumber = 1 } }
        };
        var requestTarget = JsonSerializer.Deserialize<TiaMcpServer.Network.NetworkObjectTarget>(
            CanonicalJson.Serialize(selector), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var validation = TiaMcpServer.Network.NetworkOperationCatalog.ValidateWrite(new[]
        {
            new TiaMcpServer.Network.NetworkOperationRequest
            {
                OperationId = "configure", Operation = "configure_network_device", Target = requestTarget,
                Changes = new() { IpAddress = "192.168.0.10" }
            }
        });
        Assert.True(validation.IsValid, validation.Error);
        var reply = await harness.Client.CallToolAsync("network_write", new Dictionary<string, object?> { ["operations"] = Array.Empty<object>(), ["dryRun"] = true });
        var publicRoot = Document(reply);
        Assert.Equal("1.0", publicRoot.GetProperty("contractVersion").GetString());
    }

    [Theory]
    [InlineData("confirm", "false")]
    [InlineData("acknowledge", "[]")]
    [InlineData("unknown", "true")]
    [InlineData("dryRun", "\"true\"")]
    [InlineData("dryRun", "null")]
    public async Task LegacyOrMalformedArguments_DoNotReachWorker(string key, string json)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path,
            "network-guarded");
        var before = requests.Methods();
        Assert.Contains("hello", before);
        Assert.Contains("read_hardware_config", before);
        var args = Arguments();
        args[key] = JsonSerializer.Deserialize<JsonElement>(json);
        var reply = await harness.Client.CallToolAsync("network_write", args);
        Assert.True(reply.IsError);
        Assert.Equal(before, requests.Methods());
        Assert.Empty(AuditLines(audit.Path));
    }

    [Fact]
    public async Task RegisteredSchema_OnlyOperationsAndBooleanDryRun()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path);
        var tool = Assert.Single(await harness.Client.ListToolsAsync(), t => t.Name == "network_write").ProtocolTool;
        Assert.Equal(new[] { "dryRun", "operations" }, tool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("boolean", tool.InputSchema.GetProperty("properties").GetProperty("dryRun").GetProperty("type").GetString());
        Assert.False(tool.InputSchema.GetProperty("properties").GetProperty("dryRun").GetProperty("default").GetBoolean());
        Assert.Contains("verification", tool.OutputSchema!.Value.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.Contains("omission", tool.OutputSchema.Value.GetProperty("properties").EnumerateObject().Select(p => p.Name));
    }

    [Theory]
    [InlineData(McpAccessMode.ReadWrite, null, false)]
    [InlineData(McpAccessMode.ReadWrite, false, true)]
    [InlineData(McpAccessMode.ReadWrite, true, true)]
    [InlineData(McpAccessMode.Full, null, true)]
    [InlineData(McpAccessMode.Full, false, false)]
    [InlineData(McpAccessMode.Full, true, false)]
    public async Task NetworkNeverElicits(McpAccessMode mode, bool? dryRun, bool supported)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        var prompts = 0;
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, audit.Path,
            "network-guarded", supported ? Options(() => prompts++, "decline") : null);
        var args = Arguments();
        if (dryRun.HasValue) args["dryRun"] = dryRun.Value;
        var reply = await harness.Client.CallToolAsync("network_write", args);
        var root = Document(reply);
        Assert.False(reply.IsError == true);
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("omission").ValueKind);
        Assert.Equal(dryRun == true ? "preview" : "applied", root.GetProperty("phase").GetString());
        Assert.Equal(0, prompts);
        Assert.Equal(dryRun != true, requests.Methods().Contains("delete_subnet"));
        var guard = Assert.Single(root.GetProperty("guards").EnumerateArray());
        Assert.Equal("network_delete_connected_subnet", guard.GetProperty("id").GetString());
        Assert.Equal("info", guard.GetProperty("severity").GetString());
        Assert.Equal(JsonValueKind.Null, guard.GetProperty("acknowledged").ValueKind);
        Assert.Single(AuditLines(audit.Path));
    }

    [Fact]
    public async Task LifecycleStillConfirms()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = new LifecycleProtocolFixture();
        using var uiOpen = new FakeWorkerUiOpenProject(fixture.SourcePath);
        var prompts = 0;
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite,
            audit.Path, fixture.SourcePath, Options(() => prompts++, "accept"));
        var reply = await harness.Client.CallToolAsync("save_project", new Dictionary<string, object?>());
        Assert.False(reply.IsError == true);
        Assert.Equal(1, prompts);
    }

    internal static Dictionary<string, object?> Arguments() => new()
    {
        ["operations"] = new[] { NetworkGuardedWriteFixture.Delete() }
    };
    internal static string[] AuditLines(string directory) => Directory.Exists(directory)
        ? Directory.GetFiles(directory, "writes-*.jsonl").SelectMany(File.ReadLines).ToArray() : [];
    internal static JsonElement Document(CallToolResult reply)
    {
        var root = Assert.IsType<JsonElement>(reply.StructuredContent);
        Assert.Equal(CanonicalJson.Serialize(root), Assert.Single(reply.Content.OfType<TextContentBlock>()).Text);
        return root;
    }
    private static McpClientOptions Options(Action prompt, string action) => new()
    {
        Capabilities = new() { Elicitation = new() { Form = new() } },
        Handlers = new() { ElicitationHandler = (_, _) =>
        {
            prompt();
            return ValueTask.FromResult(new ElicitResult { Action = action,
                Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) } });
        } }
    };
}
