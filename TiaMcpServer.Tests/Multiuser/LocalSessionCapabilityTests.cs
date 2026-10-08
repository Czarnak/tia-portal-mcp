using System.Text.Json;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Multiuser;

[Collection("Mcp protocol serial")]
public sealed class LocalSessionCapabilityTests
{
    private const string Amc = "C:/Projects/Local.amc21";

    [Theory]
    [InlineData("browse_project_tree")]
    [InlineData("save_project")]
    public async Task UnsupportedProjectOperation_DeniesBeforeDomainDispatch(string operation)
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, Amc));
        using var ui = new FakeWorkerUiOpenProject(Amc);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        var bound = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["projectPath"] = Amc });
        Assert.True(bound.StructuredContent!.Value.GetProperty("success").GetBoolean(), bound.StructuredContent!.Value.GetRawText());

        var before = log.Methods().Length;
        var arguments = operation == "save_project"
            ? new Dictionary<string, object?> { ["dryRun"] = true }
            : new Dictionary<string, object?>();
        var result = await harness.Client.CallToolAsync(operation, arguments);
        var document = result.StructuredContent!.Value;
        var error = operation == "browse_project_tree"
            ? document.GetProperty("failure") : document.GetProperty("error");
        if (operation == "browse_project_tree")
            Assert.Equal("failed", document.GetProperty("status").GetString());
        else
            Assert.False(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal(WorkerFailureCategories.UnsupportedCapability,
            error.GetProperty("category").GetString());
        Assert.DoesNotContain(operation, log.Methods().Skip(before));
        Assert.DoesNotContain("probe_project_status_for_lifecycle", log.Methods().Skip(before));
    }

    [Fact]
    public async Task BasicStatusAndIndependentInventory_RemainAvailable()
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, Amc));
        using var ui = new FakeWorkerUiOpenProject(Amc);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly);
        var bound = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["projectPath"] = Amc });
        Assert.True(bound.StructuredContent!.Value.GetProperty("success").GetBoolean(), bound.StructuredContent!.Value.GetRawText());

        var status = await harness.Client.CallToolAsync("get_project_status", new Dictionary<string, object?>());
        var project = status.StructuredContent!.Value.GetProperty("result").GetProperty("value");
        Assert.Equal(ProjectContainerKinds.LocalSession, project.GetProperty("context").GetProperty("containerKind").GetString());
        Assert.Equal(JsonValueKind.Null, project.GetProperty("metadata").ValueKind);

        var inventory = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["action"] = "list_server_connections", ["portalProcessId"] = 42
        });
        Assert.True(inventory.StructuredContent!.Value.GetProperty("success").GetBoolean(),
            inventory.StructuredContent!.Value.GetRawText());
    }

    [Theory]
    [InlineData("create_project")]
    [InlineData("save_project_as")]
    [InlineData("archive_project")]
    [InlineData("close_project")]
    [InlineData("compile_check")]
    [InlineData("plc_read")]
    [InlineData("network_read")]
    public async Task LocalUnsupportedOperation_DeniesBeforeWorkerDispatch(string operation)
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, Amc));
        using var ui = new FakeWorkerUiOpenProject(Amc);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        var bound = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["projectPath"] = Amc });
        Assert.True(bound.StructuredContent!.Value.GetProperty("success").GetBoolean(), bound.StructuredContent.Value.GetRawText());

        var before = log.Methods().Length;
        var arguments = operation switch
        {
            "create_project" => new Dictionary<string, object?> { ["projectDirectory"] = audit.Path, ["projectName"] = "New", ["dryRun"] = true },
            "save_project_as" => new Dictionary<string, object?> { ["targetDirectory"] = audit.Path, ["targetName"] = "Copy", ["dryRun"] = true },
            "archive_project" => new Dictionary<string, object?> { ["archiveDirectory"] = audit.Path, ["archiveName"] = "Copy", ["dryRun"] = true },
            "close_project" => new Dictionary<string, object?> { ["dryRun"] = true },
            "plc_read" => new Dictionary<string, object?> { ["operations"] = new[] { new { operationId = "r1", operation = "list_tag_tables" } } },
            "network_read" => new Dictionary<string, object?> { ["operations"] = new[] { new { operationId = "r1", operation = "read_hardware_config" } } },
            _ => new Dictionary<string, object?>()
        };
        var response = await harness.Client.CallToolAsync(operation, arguments);
        Assert.Contains(WorkerFailureCategories.UnsupportedCapability, response.StructuredContent!.Value.GetRawText());
        Assert.DoesNotContain(operation, log.Methods().Skip(before));
        Assert.DoesNotContain("probe_project_status_for_lifecycle", log.Methods().Skip(before));
    }
}
