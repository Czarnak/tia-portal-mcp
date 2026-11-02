using System.Text.Json;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Contracts.Vci;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

[Collection("Mcp protocol serial")]
public class VciGuardedWriteMcpTests
{
    internal static object Item(string operation = "export", string id = "a", string workspace = "Fixture", string? root = null)
        => new { operationId = id, operation, workspaceName = workspace, rootPath = root,
            scope = operation is "create_workspace" or "delete_workspace" ? null : new[] { new { nodeType = "Device", name = "Fixture" } } };
    internal static Dictionary<string, object?> Args(params object[] items) => new() { ["operations"] = items };

    [Theory]
    [InlineData(McpAccessMode.ReadWrite, "none")]
    [InlineData(McpAccessMode.Full, "policy")]
    public async Task PreviewApplySyncAndAudit(McpAccessMode mode, string confirmation)
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, audit.Path, "vci-write-roundtrip");
        var args = Args(Item()); args["dryRun"] = true;
        var preview = CanonicalToolDocument.Read(await h.Client.CallToolAsync("vci_write", args));
        Assert.True(preview.GetProperty("success").GetBoolean(), preview.GetRawText());
        Assert.Contains("vci_plan", requests.Methods());
        Assert.DoesNotContain("vci_list_mappings", requests.Methods());
        Assert.DoesNotContain("vci_export", requests.Methods());
        args.Remove("dryRun");
        var applied = CanonicalToolDocument.Read(await h.Client.CallToolAsync("vci_write", args));
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.Equal(preview.GetProperty("effects").GetRawText(), applied.GetProperty("effects").GetRawText());
        var sync = CanonicalToolDocument.Read(await h.Client.CallToolAsync("vci_write", Args(Item("sync_to_workspace"))));
        Assert.True(sync.GetProperty("success").GetBoolean(), sync.GetRawText());
        Assert.Equal("trusted", sync.GetProperty("verification").GetProperty("operations")[0].GetProperty("check").GetString());
        Assert.All(applied.GetProperty("verification").GetProperty("operations").EnumerateArray(), v => Assert.Equal("trusted", v.GetProperty("check").GetString()));
        Assert.DoesNotContain("vci_list_workspaces", requests.Methods());
        Assert.Equal(3, requests.Methods().Count(m => m == "vci_plan"));
        var lines = NetworkGuardedWriteMcpTests.AuditLines(audit.Path);
        Assert.Equal(3, lines.Length);
        Assert.Equal(confirmation, JsonDocument.Parse(lines[1]).RootElement.GetProperty("confirmation").GetProperty("by").GetString());
    }

    [Fact]
    public async Task FailedLeavesStopLaterItemsWithTypedCounts()
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, "vci-write-roundtrip");
        var root = CanonicalToolDocument.Read(await h.Client.CallToolAsync("vci_write", Args(Item(workspace: "Fail"), Item(id: "b"))));
        Assert.False(root.GetProperty("success").GetBoolean());
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal("failed", items[0].GetProperty("status").GetString());
        Assert.Equal(1, items[0].GetProperty("result").GetProperty("failed").GetInt32());
        Assert.Equal("skipped", items[1].GetProperty("status").GetString());
    }

    private static async Task<JsonElement> RunAsync(string scenario, params object[] items)
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, scenario);
        return CanonicalToolDocument.Read(await h.Client.CallToolAsync("vci_write", Args(items))).Clone();
    }

    [Fact]
    public async Task RejectedExportLeaf_TypedWithFailuresAndStopsLaterItems()
    {
        var root = await RunAsync("vci-write-roundtrip", Item(id: "a", workspace: "Healthy"), Item(id: "b", workspace: "Rejected"), Item(id: "c", workspace: "Healthy"));
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var items = root.GetProperty("batch").GetProperty("operations");
        var bad = items[1];
        Assert.Equal("engineering_rejected", bad.GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal("EngineeringTargetInvocationException", bad.GetProperty("failure").GetProperty("engineeringError").GetProperty("type").GetString());
        var result = bad.GetProperty("result");
        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        Assert.Equal(1, result.GetProperty("failures").GetArrayLength());
        Assert.Equal("skipped", items[2].GetProperty("status").GetString());
    }

    [Fact]
    public async Task RejectedImport_ListsThrowerWithRollbackCounts()
    {
        var root = await RunAsync("vci-write-roundtrip", Item("sync_to_project", workspace: "ImportRejected"));
        var result = root.GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Equal(2, result.GetProperty("failed").GetInt32());
        Assert.Equal(0, result.GetProperty("succeeded").GetInt32());
        Assert.False(result.GetProperty("projectModified").GetBoolean());
        Assert.Equal(1, result.GetProperty("failures").GetArrayLength());
    }

    [Fact]
    public async Task ManyRejected_DeliversCappedFailures()
    {
        var root = await RunAsync("vci-write-roundtrip", Item(workspace: "ManyRejected"));
        var result = root.GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Equal(20, result.GetProperty("failures").GetArrayLength());
        Assert.Equal(5, result.GetProperty("failuresOmitted").GetInt32());
        Assert.Equal(30, result.GetProperty("leaves").GetInt32());
        Assert.Equal(5, result.GetProperty("succeeded").GetInt32());
        Assert.Equal(25, result.GetProperty("failed").GetInt32());
        Assert.Equal(500, result.GetProperty("failures")[0].GetProperty("message").GetString()!.Length);
    }

    [Fact]
    public async Task CreateDuplicateName_IsEngineeringRejectedFromPortal()
    {
        var root = await RunAsync("vci-write-roundtrip", Item("create_workspace", workspace: "Fixture", root: Path.Combine(Path.GetTempPath(), "vci-dup-name-root")));
        Assert.False(root.GetProperty("success").GetBoolean());
        var item = root.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("engineering_rejected", item.GetProperty("failure").GetProperty("category").GetString());
        Assert.Contains("The workspace name must be unique", item.GetProperty("failure").GetProperty("engineeringError").GetProperty("message").GetString());
        Assert.DoesNotContain("vci_workspace_exists", root.GetRawText());
    }

    [Fact]
    public async Task CreateSharedRoot_IsBlockedByWorkspaceExists()
    {
        var shared = Path.Combine(Path.GetTempPath(), "vci-Fixture");
        var root = await RunAsync("vci-write-roundtrip", Item("create_workspace", workspace: "BrandNew", root: shared));
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Contains("vci_workspace_exists", root.GetRawText());
    }

    [Fact]
    public async Task PortalLostWithFailures_KeepsOutcomeAndInvalidatesBinding()
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, "vci-portal-lost");
        var root = CanonicalToolDocument.Read(await h.Client.CallToolAsync("vci_write", Args(Item(id: "bad"))));
        var item = root.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("portal_lost", item.GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal(1, item.GetProperty("result").GetProperty("failures").GetArrayLength());
        Assert.False(h.WorkerClient.BindingSnapshot.IsVerified);
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
        var reply = await h.Client.CallToolAsync("vci_write", args);
        ArgumentRejectionAssert.IsCanonicalValidationError(reply, "vci_write");
        Assert.Equal("error", reply.StructuredContent!.Value.GetProperty("phase").GetString());
        Assert.Equal("vci_write", reply.StructuredContent.Value.GetProperty("tool").GetString());
        Assert.Equal(before, log.Methods()); Assert.Empty(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Fact]
    public async Task ExportOntoExistingKey_IsNotBlocked()
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, "vci-write-roundtrip");
        var args = Args(Item(workspace: "Fixture")); args["dryRun"] = true;
        var root = CanonicalToolDocument.Read(await h.Client.CallToolAsync("vci_write", args));
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("preview", root.GetProperty("phase").GetString());
        Assert.Empty(root.GetProperty("guards").EnumerateArray());
    }

    [Fact]
    public async Task BlockedCallWritesOneAudit()
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, "vci-write-roundtrip");
        var root = CanonicalToolDocument.Read(await h.Client.CallToolAsync("vci_write", Args(Item(workspace: "Blocked"))));
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
        var root = CanonicalToolDocument.Read(await h.Client.CallToolAsync("vci_write", Args(Item(id: "ok", workspace: "Healthy"), Item(id: "bad"), Item(id: "last", workspace: "Other"))));
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
        var only = Assert.Single(root.GetProperty("verification").GetProperty("operations").EnumerateArray());
        Assert.Equal(("ok", "trusted"), (only.GetProperty("operationId").GetString(), only.GetProperty("check").GetString()));
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
        var root = CanonicalToolDocument.Read(await h.Client.CallToolAsync("vci_write", Args(new { operationId = "a", operation = "export", workspaceName = "Fixture", scope = new[]
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
        var root = CanonicalToolDocument.Read(await h.Client.CallToolAsync("vci_write", Args(Item(workspace: "OuterFailure"), Item(id: "b"))));
        var outcome = root.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("portal_lost", outcome.GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal(0, outcome.GetProperty("result").GetProperty("leaves").GetInt32());
        Assert.Equal(1, outcome.GetProperty("result").GetProperty("failed").GetInt32());
        Assert.Equal(0, root.GetProperty("verification").GetProperty("operations").GetArrayLength());
        Assert.Equal("vci_export", log.Methods().Last());
        Assert.False(h.WorkerClient.BindingSnapshot.IsVerified);
    }

    [Fact]
    public async Task SixOperationsOneWorkspace_LastTrustedEarlierSuperseded()
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, "vci-write-roundtrip");
        var items = new[] { "a", "b", "c", "d", "e", "f" }.Select(id => Item(id: id)).ToArray();
        var root = CanonicalToolDocument.Read(await h.Client.CallToolAsync("vci_write", Args(items)));
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        var checks = root.GetProperty("verification").GetProperty("operations").EnumerateArray().Select(o => o.GetProperty("check").GetString()).ToArray();
        Assert.Equal(["superseded", "superseded", "superseded", "superseded", "superseded", "trusted"], checks);
    }

    [Fact]
    public async Task CaseVariantDependent_NoPlanInPreviewOrApply()
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, "vci-write-roundtrip");
        var args = Args(Item("create_workspace", "a", "w", Path.Combine(audit.Path, "workspace")), Item("export", "b", "W")); args["dryRun"] = true;
        var preview = CanonicalToolDocument.Read(await h.Client.CallToolAsync("vci_write", args));
        Assert.True(preview.GetProperty("success").GetBoolean(), preview.GetRawText());
        Assert.Contains(preview.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("b after a") && w.GetString()!.StartsWith("Dependent operations ["));
        Assert.Single(preview.GetProperty("effects").EnumerateArray());
        Assert.Equal(1, log.Methods().Count(m => m == "vci_plan"));
        args.Remove("dryRun");
        var applied = CanonicalToolDocument.Read(await h.Client.CallToolAsync("vci_write", args));
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.Equal(2, log.Methods().Count(m => m == "vci_plan"));
        Assert.Contains("vci_create_workspace", log.Methods()); Assert.Contains("vci_export", log.Methods());
        Assert.Equal(["a", "b"], applied.GetProperty("batch").GetProperty("operations").EnumerateArray().Where(o => o.GetProperty("status").GetString() == "succeeded").Select(o => o.GetProperty("operationId").GetString()!));
        var checks = applied.GetProperty("verification").GetProperty("operations").EnumerateArray().Select(o => o.GetProperty("check").GetString()!).ToArray();
        Assert.Equal(["superseded", "trusted"], checks);
    }

    [Fact]
    public void Description_StatesContractEssentials()
    {
        var description = typeof(TiaMcpServer.Vci.VciWriteTools).GetMethod("VciWrite")!
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).Cast<System.ComponentModel.DescriptionAttribute>().Single().Description;
        foreach (var term in new[] { "failuresOmitted", "overwrite", "superseded", "compile_check", "standalone", "SimaticSD", "timeout" })
            Assert.Contains(term, description);
    }

    [Fact]
    public async Task IndependentCreatesSharingRootSecondLateCheckRefusesSafely()
    {
        using var audit = new TempAuditDirectory();
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, "vci-write-roundtrip");
        Directory.CreateDirectory(audit.Path);
        var path = Path.Combine(audit.Path, "workspace");
        var root = CanonicalToolDocument.Read(await h.Client.CallToolAsync("vci_write", Args(Item("create_workspace", "a", "NewA", path), Item("create_workspace", "b", "NewB", path))));
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal("succeeded", items[0].GetProperty("status").GetString());
        Assert.Equal("state_changed", items[1].GetProperty("failure").GetProperty("category").GetString());
        var entry = Assert.Single(root.GetProperty("verification").GetProperty("operations").EnumerateArray());
        Assert.Equal(("a", "trusted"), (entry.GetProperty("operationId").GetString(), entry.GetProperty("check").GetString()));
    }

    [Fact]
    public async Task TimeoutForeignBreadcrumbIgnoredAndRemoved()
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, "vci-timeout-silent", requestTimeout: TimeSpan.FromSeconds(2));
        Assert.True(VciBreadcrumbFile.TryWrite(h.BreadcrumbLocation.Path, new(Guid.NewGuid().ToString("N"), "foreign", "export", [new() { NodeType = "Device", Name = "FOREIGN_MARKER" }], null)));
        var root = CanonicalToolDocument.Read(await h.Client.CallToolAsync("vci_write", Args(Item())));
        Assert.DoesNotContain("FOREIGN_MARKER", root.GetRawText());
        Assert.False(File.Exists(h.BreadcrumbLocation.Path));
        var failure = root.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure");
        Assert.Equal("worker_timeout", failure.GetProperty("category").GetString());
        Assert.Contains("inspect with vci_read", failure.GetProperty("message").GetString());
        Assert.Equal("vci_export", log.Methods().Last());
    }
}
