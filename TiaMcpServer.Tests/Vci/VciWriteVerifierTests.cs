using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Tests.TestSupport;
using Xunit;
using static TiaMcpServer.Tests.Vci.VciGuardedWriteMcpTests;

namespace TiaMcpServer.Tests.Vci;

[Collection("Mcp protocol serial")]
public class VciWriteVerifierTests
{
    [Theory]
    [InlineData("delete_mapping", "Unverifiable")]
    [InlineData("export", "EmptyUnverifiable")]
    public async Task SuccessShapedUnverifiableInventoryDoesNotVerifyZeroMappings(string operation, string workspace)
    {
        using var audit = new TempAuditDirectory();
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, "vci-write-roundtrip");
        var root = Document(await h.Client.CallToolAsync("vci_write", Args(Item(operation, workspace: workspace))));
        Assert.Equal("succeeded", root.GetProperty("batch").GetProperty("operations")[0].GetProperty("status").GetString());
        Assert.False(root.GetProperty("success").GetBoolean(), root.GetRawText());
        var verification = root.GetProperty("verification");
        Assert.False(verification.GetProperty("success").GetBoolean());
        var check = Assert.Single(verification.GetProperty("operations").EnumerateArray());
        Assert.Equal("notVerified", check.GetProperty("check").GetString());
        Assert.Contains("Synthetic mapping inventory failed", check.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PartialExportWithKnownFailedLeafCollisionStillVerifiesMappedCount()
    {
        using var audit = new TempAuditDirectory();
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, "vci-write-roundtrip");
        var root = Document(await h.Client.CallToolAsync("vci_write", Args(Item(workspace: "PartialCollision"))));
        Assert.False(root.GetProperty("success").GetBoolean());
        var outcome = root.GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Equal(1, outcome.GetProperty("failed").GetInt32());
        Assert.Equal(1, outcome.GetProperty("succeeded").GetInt32());
        var verification = root.GetProperty("verification");
        Assert.True(verification.GetProperty("success").GetBoolean(), root.GetRawText());
        var check = Assert.Single(verification.GetProperty("operations").EnumerateArray());
        Assert.Equal("mappedLeaves", check.GetProperty("check").GetString());
        Assert.Equal("1", check.GetProperty("expected").GetString());
        Assert.Equal("1", check.GetProperty("actual").GetString());
    }

    [Fact]
    public async Task DeleteMappingOnUnmappedScopeVerifiesZeroWithoutCountingUnmappedLeaves()
    {
        using var audit = new TempAuditDirectory();
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, "vci-write-roundtrip");
        var root = Document(await h.Client.CallToolAsync("vci_write", Args(Item("delete_mapping"))));
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal(0, root.GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("leaves").GetInt32());
    }

    [Fact]
    public async Task UnitMappingUnderScopeDoesNotFailExportVerification()
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, "vci-write-roundtrip");
        var root = Document(await h.Client.CallToolAsync("vci_write", Args(Item(workspace: "UnitMapping"))));
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("2", root.GetProperty("verification").GetProperty("operations")[0].GetProperty("actual").GetString());
        Assert.DoesNotContain("vci_list_mappings", log.Methods());
        Assert.DoesNotContain("vci_list_workspaces", log.Methods());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyLastWorkspaceItemVerified(bool createDelete)
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, "vci-write-roundtrip");
        var items = createDelete ? new[] { Item("create_workspace", "a", "New", Path.Combine(audit.Path, "workspace")), Item("delete_workspace", "b", "New") }
            : new[] { Item(), Item("delete_mapping", "b") };
        var root = Document(await h.Client.CallToolAsync("vci_write", Args(items)));
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        var checks = root.GetProperty("verification").GetProperty("operations");
        Assert.Equal("superseded", checks[0].GetProperty("check").GetString());
        Assert.Equal(createDelete ? "absent" : "mappedLeaves", checks[1].GetProperty("check").GetString());
        Assert.DoesNotContain("vci_list_mappings", log.Methods());
        Assert.Equal(createDelete ? 2 : 3, log.Methods().Count(m => m == "vci_plan"));
    }

    [Fact]
    public async Task DeferredPreviewNamesUnknownInventoryThenApplyReplansAfterCreate()
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, "vci-write-roundtrip");
        var args = Args(Item("create_workspace", "a", "New", Path.Combine(audit.Path, "workspace")), Item("export", "b", "New")); args["dryRun"] = true;
        var preview = Document(await h.Client.CallToolAsync("vci_write", args));
        Assert.True(preview.GetProperty("success").GetBoolean(), preview.GetRawText());
        Assert.Contains(preview.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("[b]") && w.GetString()!.Contains("await predecessor mutation"));
        Assert.Single(preview.GetProperty("effects").EnumerateArray());
        Assert.Equal(1, log.Methods().Count(m => m == "vci_plan"));
        args.Remove("dryRun");
        var applied = Document(await h.Client.CallToolAsync("vci_write", args));
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.Equal(2, applied.GetProperty("effects")[1].GetProperty("effect").GetProperty("leaves").GetInt32());
    }

    [Fact]
    public async Task IndependentCreatesSharingRootSecondLateCheckRefusesSafely()
    {
        using var audit = new TempAuditDirectory();
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, "vci-write-roundtrip");
        Directory.CreateDirectory(audit.Path);
        var path = Path.Combine(audit.Path, "workspace");
        var root = Document(await h.Client.CallToolAsync("vci_write", Args(Item("create_workspace", "a", "NewA", path), Item("create_workspace", "b", "NewB", path))));
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal("succeeded", items[0].GetProperty("status").GetString());
        Assert.Equal("state_changed", items[1].GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal("notVerified", root.GetProperty("verification").GetProperty("operations")[1].GetProperty("check").GetString());
    }

    [Fact]
    public async Task TimeoutForeignBreadcrumbIgnoredAndRemoved()
    {
        using var audit = new TempAuditDirectory(); Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var h = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, "vci-timeout-silent", requestTimeout: TimeSpan.FromSeconds(2));
        Assert.True(VciBreadcrumbFile.TryWrite(h.BreadcrumbLocation.Path, new(Guid.NewGuid().ToString("N"), "foreign", "export", [new() { NodeType = "Device", Name = "FOREIGN_MARKER" }], null)));
        var root = Document(await h.Client.CallToolAsync("vci_write", Args(Item())));
        Assert.DoesNotContain("FOREIGN_MARKER", root.GetRawText());
        Assert.False(File.Exists(h.BreadcrumbLocation.Path));
        var failure = root.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure");
        Assert.Equal("worker_timeout", failure.GetProperty("category").GetString());
        Assert.Contains("inspect with vci_read", failure.GetProperty("message").GetString());
        Assert.Equal("vci_export", log.Methods().Last());
    }
}
