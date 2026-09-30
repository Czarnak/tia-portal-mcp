using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Batch;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Safety;
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
    public async Task OpenProject_ProtocolPreview_ReturnsSafetyTokenThroughRegisteredTool()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectWriteTools>(accessMode: McpAccessMode.Full);
        const string destination = @"C:\open\Line.ap21";

        var result = await harness.Client.CallToolAsync(
            "open_project",
            new Dictionary<string, object?>
            {
                ["projectPath"] = destination
            });

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("safetyToken", text, StringComparison.Ordinal);
        Assert.Contains("open_project", text, StringComparison.Ordinal);
        Assert.Contains("Preview only", text, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var target = root.GetProperty("target");
        Assert.Equal(JsonValueKind.Null, target.GetProperty("sourceProjectPath").ValueKind);
        Assert.Equal(destination, target.GetProperty("destinationProjectPath").GetString());
        Assert.Equal(WriteSafetyService.HashText(WriteSafetyService.ToStableJson(
            new { projectPath = destination, forceRebind = false })),
            root.GetProperty("requestedInputHash").GetString());
    }
}
