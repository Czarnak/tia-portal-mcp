using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

[Collection("Mcp protocol serial")]
public class WriteToolMcpAnnotationProtocolTests
{
    private static readonly string[] ReadOnlyToolNames =
    {
        "browse_project_tree",
        "execute_read_batch",
        "get_project_status",
        "network_read",
    };

    private static readonly string[] ReadWriteToolNames =
    {
        "apply_write_batch",
        "archive_project",
        "browse_project_tree",
        "close_project",
        "compile_check",
        "create_project",
        "execute_read_batch",
        "get_project_status",
        "network_read",
        "network_write",
        "open_project",
        "preview_write_batch",
        "save_project",
        "save_project_as",
    };

    [Fact]
    public async Task ToolsList_ReadWriteProductionSurface_ExposesExactNamesCountsAnnotations_AndRepresentativeSchemas()
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite);
        var tools = (await harness.Client.ListToolsAsync()).OrderBy(tool => tool.Name).ToArray();
        var byName = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

        Assert.Equal(ReadWriteToolNames, tools.Select(tool => tool.Name));
        Assert.Equal(14, tools.Length);

        var previewAnnotations = byName["preview_write_batch"].ProtocolTool.Annotations;
        Assert.NotNull(previewAnnotations);
        Assert.True(previewAnnotations!.ReadOnlyHint);
        Assert.False(previewAnnotations.DestructiveHint);
        Assert.False(previewAnnotations.OpenWorldHint);

        var applyAnnotations = byName["apply_write_batch"].ProtocolTool.Annotations;
        Assert.NotNull(applyAnnotations);
        Assert.False(applyAnnotations!.ReadOnlyHint);
        Assert.True(applyAnnotations.DestructiveHint);
        Assert.False(applyAnnotations.OpenWorldHint);

        var openAnnotations = byName["open_project"].ProtocolTool.Annotations;
        Assert.NotNull(openAnnotations);
        Assert.False(openAnnotations!.ReadOnlyHint);
        Assert.True(openAnnotations.DestructiveHint);
        Assert.False(openAnnotations.OpenWorldHint);

        Assert.Contains("\"operations\"", byName["preview_write_batch"].ProtocolTool.InputSchema.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("\"projectPath\"", byName["open_project"].ProtocolTool.InputSchema.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("\"confirm\"", byName["open_project"].ProtocolTool.InputSchema.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("\"safetyToken\"", byName["open_project"].ProtocolTool.InputSchema.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolsList_ReadOnlyProductionSurface_ExposesExactReadOnlyTools_AndNoWriteTools()
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly);
        var tools = (await harness.Client.ListToolsAsync()).OrderBy(tool => tool.Name).ToArray();
        var toolNames = tools.Select(tool => tool.Name).ToArray();

        Assert.Equal(ReadOnlyToolNames, toolNames);
        Assert.Equal(4, tools.Length);

        foreach (var writeToolName in ReadWriteToolNames.Except(ReadOnlyToolNames, StringComparer.Ordinal))
        {
            Assert.DoesNotContain(writeToolName, toolNames);
        }
    }
}
