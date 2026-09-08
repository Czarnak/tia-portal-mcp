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
        var inputSchema = tool.ProtocolTool.InputSchema;
        Assert.False(inputSchema.GetProperty("additionalProperties").GetBoolean());
        var selectorItems = inputSchema.GetProperty("properties").GetProperty("startSelector").GetProperty("items");
        Assert.False(selectorItems.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "name", "nodeType" },
            selectorItems.GetProperty("required").EnumerateArray()
                .Select(item => item.GetString()).Order().ToArray());

        var result = await CallAsync(harness, new Dictionary<string, object?>
        {
            ["projectPath"] = "project-tree-v3-small",
            ["pageSize"] = 2,
        });

        var structured = AssertOneCanonicalDocument(result);
        Assert.False(result.IsError, structured.GetRawText());
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

    [Theory]
    [InlineData("startPath")]
    [InlineData("deviceName")]
    [InlineData("plcName")]
    [InlineData("unexpected")]
    public async Task BrowseProjectTree_UnknownTopLevelArgumentReturnsCanonicalValidationFailure(
        string unknownArgument)
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectReadTools>();
        var arguments = new Dictionary<string, object?>
        {
            ["projectPath"] = "project-tree-v3-small",
            [unknownArgument] = "must-not-be-ignored",
        };

        var result = await CallAsync(harness, arguments);

        AssertValidationFailure(result);
    }

    [Fact]
    public async Task BrowseProjectTree_UnknownSelectorMemberReturnsCanonicalSelectorFailure()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectReadTools>();
        var result = await CallAsync(harness, new Dictionary<string, object?>
        {
            ["startSelector"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["nodeType"] = ProjectTreeNodeTypes.Device,
                    ["name"] = "PLC_1",
                    ["unexpected"] = "must-not-be-ignored",
                },
            },
        });

        AssertValidationFailure(result, WorkerFailureCategories.InvalidSelector);
    }

    [Theory]
    [InlineData("startSelector", "{}", "invalid_selector")]
    [InlineData("startSelector", "[null]", "invalid_selector")]
    [InlineData("startSelector", "[{\"nodeType\":\"Device\"}]", "invalid_selector")]
    [InlineData("startSelector", "[{\"nodeType\":12,\"name\":\"PLC\"}]", "invalid_selector")]
    [InlineData("startSelector", "[]", "invalid_selector")]
    [InlineData("cursor", "\"\"", "invalid_cursor")]
    [InlineData("cursor", "\" \"", "invalid_cursor")]
    [InlineData("cursor", "123", "invalid_cursor")]
    [InlineData("depth", "\"two\"", "validation_error")]
    [InlineData("projectPath", "[]", "validation_error")]
    public async Task BrowseProjectTree_RawArgumentDefectsKeepTheirDomainCategory(string argument, string json, string category)
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectReadTools>();
        var result = await CallAsync(harness, new Dictionary<string, object?>
        {
            [argument] = JsonSerializer.Deserialize<JsonElement>(json),
        });
        AssertValidationFailure(result, category);
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

    private static void AssertValidationFailure(CallToolResult result, string category = WorkerFailureCategories.ValidationError)
    {
        var structured = AssertOneCanonicalDocument(result);
        Assert.True(result.IsError);
        Assert.Equal(JsonValueKind.Null, structured.GetProperty("result").ValueKind);
        Assert.Equal(
            category,
            structured.GetProperty("failure").GetProperty("category").GetString());
    }
}
