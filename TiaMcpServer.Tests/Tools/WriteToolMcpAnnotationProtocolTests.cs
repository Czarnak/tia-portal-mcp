using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

[Collection("Mcp protocol serial")]
public class WriteToolMcpAnnotationProtocolTests
{
    private static readonly string[] ReadOnlyToolNames =
    {
        "bind_project",
        "browse_project_tree",
        "execute_read_batch",
        "get_project_status",
        "network_read",
        "plc_read",
        "read_cross_references",
    };

    private static readonly string[] FullToolNames =
    {
        "apply_write_batch",
        "archive_project",
        "bind_project",
        "browse_project_tree",
        "close_project",
        "compile_check",
        "create_project",
        "execute_read_batch",
        "get_project_status",
        "network_read",
        "network_write",
        "open_project",
        "plc_read",
        "preview_write_batch",
        "read_cross_references",
        "save_project",
        "save_project_as",
    };

    private static readonly (string Name, bool ReadOnly, bool Destructive, bool? Idempotent, bool OpenWorld)[] ExpectedWriteToolAnnotations =
    {
        ("bind_project", false, false, true, false),
        ("preview_write_batch", true, false, null, false),
        ("apply_write_batch", false, true, null, false),
        ("open_project", false, true, null, false),
        ("create_project", false, true, null, false),
        ("save_project", false, true, null, false),
        ("save_project_as", false, true, null, false),
        ("archive_project", false, true, null, false),
        ("close_project", false, true, null, false),
    };

    [Fact]
    public async Task ToolsList_FullProductionSurface_ExposesExactNamesCountsAnnotations_AndRepresentativeSchemas()
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full);
        var tools = (await harness.Client.ListToolsAsync()).OrderBy(tool => tool.Name).ToArray();
        var byName = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

        Assert.Equal(FullToolNames, tools.Select(tool => tool.Name));
        Assert.Equal(17, tools.Length);
        Assert.All(
            tools,
            tool => Assert.Equal(
                System.Text.Json.JsonValueKind.Object,
                tool.ProtocolTool.InputSchema.ValueKind));

        foreach (var expected in ExpectedWriteToolAnnotations)
        {
            var annotations = byName[expected.Name].ProtocolTool.Annotations;
            Assert.NotNull(annotations);
            Assert.Equal(expected.ReadOnly, annotations!.ReadOnlyHint);
            Assert.Equal(expected.Destructive, annotations.DestructiveHint);
            Assert.Equal(expected.Idempotent, annotations.IdempotentHint);
            Assert.Equal(expected.OpenWorld, annotations.OpenWorldHint);
        }

        Assert.Contains("\"operations\"", byName["preview_write_batch"].ProtocolTool.InputSchema.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("\"projectPath\"", byName["open_project"].ProtocolTool.InputSchema.GetRawText(), StringComparison.Ordinal);
        foreach (var name in new[] { "open_project", "create_project", "save_project", "save_project_as", "archive_project", "close_project" })
        {
            var properties = byName[name].ProtocolTool.InputSchema.GetProperty("properties");
            Assert.True(properties.TryGetProperty("dryRun", out _));
            Assert.False(properties.TryGetProperty("acknowledge", out _));
            Assert.False(properties.TryGetProperty("confirm", out _));
            Assert.False(properties.TryGetProperty("safetyToken", out _));
            Assert.False(properties.TryGetProperty("server", out _));
            Assert.False(properties.TryGetProperty("options", out _));
            Assert.False(properties.TryGetProperty("execution", out _));
            Assert.NotNull(byName[name].ProtocolTool.OutputSchema);
        }
    }

    [Fact]
    public async Task ToolsList_ReadOnlyProductionSurface_ExposesExactReadOnlyTools_AndNoWriteTools()
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly);
        var tools = (await harness.Client.ListToolsAsync()).OrderBy(tool => tool.Name).ToArray();
        var toolNames = tools.Select(tool => tool.Name).ToArray();

        Assert.Equal(ReadOnlyToolNames, toolNames);
        Assert.Equal(7, tools.Length);

        foreach (var writeToolName in FullToolNames.Except(ReadOnlyToolNames, StringComparer.Ordinal))
        {
            Assert.DoesNotContain(writeToolName, toolNames);
        }
    }
}
