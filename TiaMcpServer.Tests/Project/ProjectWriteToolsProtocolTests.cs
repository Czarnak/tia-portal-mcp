using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectWriteToolsProtocolTests
{
    [Fact]
    public async Task RegisteredWriteTools_ToolsList_AdvertisesExactlyEightWriteTools()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectWriteTools, WriteBatchTools>(accessMode: McpAccessMode.Full);

        var names = (await harness.Client.ListToolsAsync())
            .Select(tool => tool.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "apply_write_batch",
                "archive_project",
                "close_project",
                "create_project",
                "open_project",
                "preview_write_batch",
                "save_project",
                "save_project_as"
            },
            names);
    }

    [Fact]
    public async Task OpenProject_DryRun_ReturnsCanonicalPreviewThroughRegisteredTool()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = new LifecycleProtocolFixture();
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectWriteTools>(
            auditDirectory: audit.Path, accessMode: McpAccessMode.Full);
        var before = harness.WorkerClient.BindingSnapshot;

        var result = await harness.Client.CallToolAsync(
            "open_project",
            new Dictionary<string, object?>
            {
                ["projectPath"] = fixture.DestinationPath,
                ["dryRun"] = true
            });

        var document = LifecycleTestCalls.Document(result);
        Assert.False(result.IsError == true);
        LifecycleTestCalls.Succeeded(document);
        LifecycleTestCalls.Phase(document, "preview");
        Assert.Equal("open_project", document.GetProperty("tool").GetString());
        Assert.False(document.TryGetProperty("safetyToken", out _));
        Assert.Equal(JsonValueKind.Null, document.GetProperty("error").ValueKind);
        var effects = document.GetProperty("effects");
        Assert.Equal(JsonValueKind.Null, effects.GetProperty("sourceProjectPath").ValueKind);
        Assert.Equal(fixture.DestinationPath, effects.GetProperty("destinationProjectPath").GetString());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("result").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.GetProperty("verification").ValueKind);
        Assert.True(before.SameBinding(harness.WorkerClient.BindingSnapshot));
        Assert.Equal(1, LifecycleTestCalls.AuditCount(audit));
    }
}
