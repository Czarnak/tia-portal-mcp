using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Json;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Tools;

/// <summary>Read-only project tools exposed in both access modes.</summary>
[McpServerToolType]
public class ProjectReadTools
{
    [McpServerTool(Name = "get_project_status", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(GetProjectStatusResponse))]
    [Description("Get status and metadata for the bound project, or for the sole open project when none is bound. Never binds.")]
    public static async Task<CallToolResult> GetProjectStatus(
        OpennessWorkerClient workerClient)
    {
        var result = await workerClient.GetProjectStatusAsync(null).ConfigureAwait(false);
        var rejection = StandalonePayloadContract.Rejection(result);
        return StructuredStandaloneResult.Create("get_project_status",
            rejection is null ? StandalonePayloadContract.DecodeStatus(result) : null,
            result.Warnings, rejection);
    }

    [McpServerTool(
        Name = "browse_project_tree",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(BrowseProjectTreeResponse))]
    [Description("Browse a point-in-time tree of the bound TIA project through typed, bounded, resumable flat-node pages.")]
    public static async Task<CallToolResult> BrowseProjectTree(
        ProjectTreeBrowseCoordinator coordinator,
        [Description("Optional ordered selector segments { nodeType, name }. Names match case-insensitively, node types exactly, and each segment must identify one direct child. On continuation, omit it or repeat the equivalent selector.")] ProjectTreeSelectorSegment[]? startSelector = null,
        [Description("Optional maximum depth from the selected root. Must be 1 or greater. On continuation, omit it or repeat the same depth.")] int? depth = null,
        [Description("Optional number of flat nodes requested for this page, from 1 through 200; defaults to 100 and may change between continuation pages.")] int? pageSize = null,
        [Description("Opaque cursor for the same point-in-time snapshot. Cursors can be replayed until idle expiry or eviction, but become unavailable after the server process restarts; restart without a cursor to observe again.")] string? cursor = null)
    {
        var rendered = await coordinator.BrowseAsync(
            new ProjectTreeBrowseRequest(StartSelector: startSelector, Depth: depth, PageSize: pageSize, Cursor: cursor)).ConfigureAwait(false);
        return StructuredToolResult.CreateCanonical(rendered.CanonicalText, isError: !rendered.IsSuccess);
    }
}

internal static class ProjectReadToolRegistration
{
    internal static JsonNode AlignSelectorSchemaNullability(
        AIJsonSchemaCreateContext context,
        JsonNode schema)
    {
        if (context.TypeInfo.Type == typeof(ProjectTreeSelectorSegment)
            && schema is JsonObject selectorSchema
            && selectorSchema["properties"] is JsonObject properties)
        {
            RemoveNullType(selectorSchema);
            RemoveNullType(properties["nodeType"]);
            RemoveNullType(properties["name"]);
        }

        return schema;
    }

    private static void RemoveNullType(JsonNode? schema)
    {
        if (schema is not JsonObject schemaObject
            || schemaObject["type"] is not JsonArray types)
        {
            return;
        }

        for (var index = types.Count - 1; index >= 0; index--)
        {
            if (types[index]?.GetValue<string>() == "null")
            {
                types.RemoveAt(index);
            }
        }
    }
}

internal sealed class ProjectTreeArgumentValidatingTool(McpServerTool innerTool)
    : DelegatingMcpServerTool(innerTool)
{
    private const string FixedFailureText = "The browse_project_tree arguments did not match the declared input schema.";

    private readonly IReadOnlySet<string> _rootKeys = StrictArgumentsTool.RootKeys(innerTool);

    public override ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        var (category, message) = ShapeFailure(request.Params.Arguments);
        return category is null
            ? base.InvokeAsync(request, cancellationToken)
            : ValueTask.FromResult(ValidationFailure(category, message!));
    }

    private (string? Category, string? Message) ShapeFailure(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null)
        {
            return (null, null);
        }

        var unknown = arguments.Keys.FirstOrDefault(key => !_rootKeys.Contains(key));
        if (unknown is not null)
        {
            return Invalid($"unknown argument '{unknown}'");
        }

        foreach (var (name, isValid) in new (string, bool)[]
                 {
                     ("depth", IsOptionalInteger(arguments, "depth")),
                     ("pageSize", IsOptionalInteger(arguments, "pageSize")),
                 })
        {
            if (!isValid)
            {
                return Invalid($"argument '{name}' is malformed");
            }
        }
        if (!IsOptionalString(arguments, "cursor"))
        {
            return Fixed(WorkerFailureCategories.InvalidCursor);
        }

        if (!arguments.TryGetValue("startSelector", out var selector)
            || selector.ValueKind == JsonValueKind.Null)
        {
            return (null, null);
        }

        if (selector.ValueKind != JsonValueKind.Array)
        {
            return Fixed(WorkerFailureCategories.InvalidSelector);
        }

        foreach (var segment in selector.EnumerateArray())
        {
            if (segment.ValueKind != JsonValueKind.Object)
            {
                return Fixed(WorkerFailureCategories.InvalidSelector);
            }

            var properties = segment.EnumerateObject().ToArray();
            if (properties.Length != 2
                || !segment.TryGetProperty("nodeType", out var nodeType)
                || nodeType.ValueKind != JsonValueKind.String
                || !segment.TryGetProperty("name", out var name)
                || name.ValueKind != JsonValueKind.String)
            {
                return Fixed(WorkerFailureCategories.InvalidSelector);
            }
        }

        return (null, null);
    }

    private (string, string) Invalid(string detail)
        => (WorkerFailureCategories.ValidationError,
            StrictArgumentsTool.Message("browse_project_tree", detail, _rootKeys));

    private static (string, string) Fixed(string category) => (category, FixedFailureText);
    private static bool IsOptionalString(
        IDictionary<string, JsonElement> arguments,
        string name)
        => !arguments.TryGetValue(name, out var value)
           || value.ValueKind is JsonValueKind.Null or JsonValueKind.String;

    private static bool IsOptionalInteger(
        IDictionary<string, JsonElement> arguments,
        string name)
        => !arguments.TryGetValue(name, out var value)
           || value.ValueKind == JsonValueKind.Null
           || (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _));

    internal static CallToolResult ValidationFailure(string category, string message)
    {
        var response = new BrowseProjectTreeResponse(
            ProjectTreeContract.Version,
            ProjectTreeStatuses.Failed,
            Result: null,
            new BrowseProjectTreeFailure(category, message),
            Array.Empty<string>());
        return StructuredToolResult.CreateCanonical(
            CanonicalJson.Serialize(response),
            isError: true);
    }
}
