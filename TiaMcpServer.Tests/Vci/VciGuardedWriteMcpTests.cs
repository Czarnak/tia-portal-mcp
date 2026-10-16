using System.Text.Json;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

[Collection("Mcp protocol serial")]
public class VciGuardedWriteMcpTests
{
    internal static object Item(string operation = "export", string id = "a", string workspace = "Fixture", string? root = null)
        => new { operationId = id, operation, workspaceName = workspace, rootPath = root,
            scope = operation is "create_workspace" or "delete_workspace" ? null : new[] { new { nodeType = "Device", name = "Fixture" } } };
    internal static Dictionary<string, object?> Args(params object[] items) => new() { ["operations"] = items };
    internal static JsonElement Document(ModelContextProtocol.Protocol.CallToolResult reply) => NetworkGuardedWriteMcpTests.Document(reply);

    [Theory]
    [InlineData(McpAccessMode.ReadWrite, "none")]
    [InlineData(McpAccessMode.Full, "policy")]
    public async Task PreviewApplySyncAndAudit(McpAccessMode mode, string confirmation)
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, audit.Path, "vci-write-roundtrip");
        var args = Args(Item()); args["dryRun"] = true;
        var preview = Document(await h.Client.CallToolAsync("vci_write", args));
        Assert.True(preview.GetProperty("success").GetBoolean(), preview.GetRawText());
        Assert.Contains("vci_plan", requests.Methods());
        Assert.DoesNotContain("vci_list_mappings", requests.Methods());
        Assert.DoesNotContain("vci_export", requests.Methods());
        args.Remove("dryRun");
        var applied = Document(await h.Client.CallToolAsync("vci_write", args));
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.Equal(preview.GetProperty("effects").GetRawText(), applied.GetProperty("effects").GetRawText());
        var sync = Document(await h.Client.CallToolAsync("vci_write", Args(Item("sync_to_workspace"))));
        Assert.True(sync.GetProperty("success").GetBoolean(), sync.GetRawText());
        Assert.Equal("notStatusVerified", sync.GetProperty("verification").GetProperty("operations")[0].GetProperty("check").GetString());
        var lines = NetworkGuardedWriteMcpTests.AuditLines(audit.Path);
        Assert.Equal(3, lines.Length);
        Assert.Equal(confirmation, JsonDocument.Parse(lines[1]).RootElement.GetProperty("confirmation").GetProperty("by").GetString());
    }

    [Fact]
    public async Task FailedLeavesStopLaterItemsWithTypedCounts()
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, "vci-write-roundtrip");
        var root = Document(await h.Client.CallToolAsync("vci_write", Args(Item(workspace: "Fail"), Item(id: "b"))));
        Assert.False(root.GetProperty("success").GetBoolean());
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal("failed", items[0].GetProperty("status").GetString());
        Assert.Equal(1, items[0].GetProperty("result").GetProperty("failed").GetInt32());
        Assert.Equal("skipped", items[1].GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("confirm", "true")]
    [InlineData("acknowledge", "[]")]
    [InlineData("unknown", "true")]
    [InlineData("dryRun", "null")]
    [InlineData("dryRun", "\"true\"")]
    public async Task StrictArgumentsRejectBeforeWorkerAndAudit(string key, string value)
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path);
        var before = log.Methods(); var args = Args(Item()); args[key] = JsonSerializer.Deserialize<JsonElement>(value);
        Assert.True((await h.Client.CallToolAsync("vci_write", args)).IsError);
        Assert.Equal(before, log.Methods()); Assert.Empty(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Fact]
    public async Task BlockedCallWritesOneAudit()
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, "vci-write-roundtrip");
        var root = Document(await h.Client.CallToolAsync("vci_write", Args(Item(workspace: "Blocked"))));
        Assert.Equal("blocked", root.GetProperty("phase").GetString());
        Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Theory]
    [InlineData("vci-portal-lost", "portal_lost")]
    [InlineData("vci-timeout", "worker_timeout")]
    [InlineData("vci-crash", "worker_crashed")]
    public async Task FatalFailureInvalidatesBindingAndNeverVerifies(string scenario, string category)
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, scenario, requestTimeout: TimeSpan.FromSeconds(2));
        var root = Document(await h.Client.CallToolAsync("vci_write", Args(Item(id: "ok", workspace: "Healthy"), Item(id: "bad"), Item(id: "last", workspace: "Other"))));
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal(category, items[1].GetProperty("failure").GetProperty("category").GetString());
        if (category == "portal_lost")
        {
            var last = items[1].GetProperty("result").GetProperty("lastAttempted");
            Assert.Equal("Fixture", last.GetProperty("path")[0].GetProperty("name").GetString());
            Assert.Equal(JsonValueKind.Null, last.GetProperty("format").ValueKind);
        }
        else Assert.Contains("lastAttempted:", items[1].GetProperty("failure").GetProperty("message").GetString());
        Assert.Contains("lastAttempted", Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
        Assert.False(File.Exists(h.BreadcrumbLocation.Path));
        Assert.All(root.GetProperty("verification").GetProperty("operations").EnumerateArray(), v => Assert.Equal("notVerified", v.GetProperty("check").GetString()));
        var methods = log.Methods();
        Assert.Equal("vci_export", methods.Last());
        Assert.Equal(3, methods.Count(m => m == "vci_plan"));
        Assert.False(h.WorkerClient.BindingSnapshot.IsVerified);
    }

    [Theory]
    [InlineData("SoftwareUnit")]
    [InlineData("SystemBlockFolder")]
    public async Task TypedScopeRejectionPrecedesWorker(string nodeType)
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        var before = log.Methods();
        var root = Document(await h.Client.CallToolAsync("vci_write", Args(new { operationId = "a", operation = "export", workspaceName = "Fixture", scope = new[]
        { new { nodeType = "Device", name = "Fixture" }, new { nodeType = "PlcSoftware", name = "Fixture" }, new { nodeType, name = "Fixture" } } })));
        Assert.Equal("target_kind_unsupported", root.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(before, log.Methods());
        Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Fact]
    public async Task PortalLostBeforeExpansionPreservesFailureAndSuppressesAllReads()
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, "vci-portal-lost");
        var root = Document(await h.Client.CallToolAsync("vci_write", Args(Item(workspace: "OuterFailure"), Item(id: "b"))));
        var outcome = root.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("portal_lost", outcome.GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal(0, outcome.GetProperty("result").GetProperty("leaves").GetInt32());
        Assert.Equal(1, outcome.GetProperty("result").GetProperty("failed").GetInt32());
        Assert.All(root.GetProperty("verification").GetProperty("operations").EnumerateArray(), v => Assert.Equal("notVerified", v.GetProperty("check").GetString()));
        Assert.Equal("vci_export", log.Methods().Last());
        Assert.False(h.WorkerClient.BindingSnapshot.IsVerified);
    }
}
