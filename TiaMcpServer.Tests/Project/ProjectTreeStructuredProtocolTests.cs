using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests.Project;

/// <summary>Public MCP protocol coverage for the atomic project-tree v3 cutover.</summary>
[Collection("Mcp protocol serial")]
public sealed class ProjectTreeStructuredProtocolTests
{
    [Fact]
    public async Task BrowseProjectTree_AdvertisesOutputSchemaAndUsesOneCanonicalDocument()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectReadTools>();
        var tool = Assert.Single(
            await harness.Client.ListToolsAsync(),
            candidate => candidate.Name == "browse_project_tree");
        Assert.NotNull(tool.ProtocolTool.OutputSchema);

        var result = await CallAsync(harness, new Dictionary<string, object?>
        {
            ["projectPath"] = "project-tree-v3-small",
            ["pageSize"] = 2,
        });

        var structured = AssertOneCanonicalDocument(result);
        Assert.False(result.IsError);
        Assert.Equal(JsonValueKind.Object, structured.GetProperty("result").ValueKind);
        Assert.Equal(JsonValueKind.Null, structured.GetProperty("failure").ValueKind);
        Assert.Equal(JsonValueKind.Array, structured.GetProperty("result").GetProperty("nodes").ValueKind);
    }

    [Fact]
    public async Task BrowseProjectTree_FailureEnvelopeIsMutuallyExclusive()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectReadTools>();

        var result = await CallAsync(harness, new Dictionary<string, object?> { ["pageSize"] = 0 });

        var structured = AssertOneCanonicalDocument(result);
        Assert.True(result.IsError);
        Assert.Equal(JsonValueKind.Null, structured.GetProperty("result").ValueKind);
        Assert.Equal(JsonValueKind.Object, structured.GetProperty("failure").ValueKind);
        Assert.Equal(
            WorkerFailureCategories.ValidationError,
            structured.GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public async Task BrowseProjectTree_MalformedWorkerPayloadIsNeverEchoed()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectReadTools>();

        var result = await CallAsync(harness, new Dictionary<string, object?>
        {
            ["projectPath"] = "project-tree-v3-malformed",
        });

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        var structured = AssertOneCanonicalDocument(result);
        Assert.True(result.IsError);
        Assert.Equal(
            WorkerFailureCategories.ProtocolError,
            structured.GetProperty("failure").GetProperty("category").GetString());
        Assert.DoesNotContain("PROJECT_TREE_SECRET_MARKER", text, StringComparison.Ordinal);
    }

    private static ValueTask<CallToolResult> CallAsync(
        McpProtocolTestHarness harness,
        IReadOnlyDictionary<string, object?> arguments)
        => harness.Client.CallToolAsync("browse_project_tree", arguments);

    private static JsonElement AssertOneCanonicalDocument(CallToolResult result)
    {
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(text, structured.GetRawText());
        using var parsed = JsonDocument.Parse(text);
        Assert.True(JsonElement.DeepEquals(structured, parsed.RootElement));
        return structured;
    }
}
