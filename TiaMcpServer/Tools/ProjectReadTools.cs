using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools;

/// <summary>Read-only project tools exposed in both access modes.</summary>
[McpServerToolType]
public class ProjectReadTools
{
    [McpServerTool(Name = "get_project_status", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Get status and metadata for the active TIA Portal project.")]
    public static async Task<string> GetProjectStatus(
        OpennessWorkerClient workerClient,
        [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
    {
        var result = await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false);
        return StandaloneToolResultFormatter.Format(
            result,
            "Extended metadata (history, comments, languages) was too large to return in full.");
    }

    [McpServerTool(
        Name = "browse_project_tree",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(BrowseProjectTreeResponse))]
    [Description("Browse a point-in-time TIA project tree through typed, bounded, resumable flat-node pages.")]
    public static async Task<CallToolResult> BrowseProjectTree(
        ProjectTreeBrowseCoordinator coordinator,
        [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal. On continuation, omit it or repeat the same project.")] string? projectPath = null,
        [Description("Optional ordered selector segments { nodeType, name }. Names match case-insensitively, node types exactly, and each segment must identify one direct child. On continuation, omit it or repeat the equivalent selector.")] ProjectTreeSelectorSegment[]? startSelector = null,
        [Description("Optional maximum depth from the selected root. Must be 1 or greater. On continuation, omit it or repeat the same depth.")] int? depth = null,
        [Description("Optional number of flat nodes requested for this page, from 1 through 200; defaults to 100 and may change between continuation pages.")] int? pageSize = null,
        [Description("Opaque cursor for the same point-in-time snapshot. Cursors can be replayed until idle expiry or eviction, but become unavailable after the server process restarts; restart without a cursor to observe again.")] string? cursor = null)
    {
        var rendered = await coordinator.BrowseAsync(
            new ProjectTreeBrowseRequest(projectPath, startSelector, depth, pageSize, cursor)).ConfigureAwait(false);
        return StructuredToolResult.CreateCanonical(rendered.CanonicalText, isError: !rendered.IsSuccess);
    }
}

internal static class ProjectReadToolRegistration
{
    internal static IMcpServerBuilder WithProjectReadTools(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (var method in typeof(ProjectReadTools)
                     .GetMethods(BindingFlags.Public | BindingFlags.Static)
                     .Where(candidate => candidate.GetCustomAttribute<McpServerToolAttribute>() is not null)
                     .OrderBy(candidate => candidate.MetadataToken))
        {
            var toolMethod = method;
            builder.Services.AddSingleton<McpServerTool>(services =>
            {
                var tool = McpServerTool.Create(
                    toolMethod,
                    target: null,
                    options: new McpServerToolCreateOptions
                    {
                        Services = services,
                        SchemaCreateOptions = new AIJsonSchemaCreateOptions
                        {
                            TransformOptions = new AIJsonSchemaTransformOptions
                            {
                                DisallowAdditionalProperties = true,
                            },
                        },
                    });

                return toolMethod.Name == nameof(ProjectReadTools.BrowseProjectTree)
                    ? new ProjectTreeArgumentValidatingTool(tool)
                    : tool;
            });
        }

        return builder;
    }
}

internal sealed class ProjectTreeArgumentValidatingTool(McpServerTool innerTool)
    : DelegatingMcpServerTool(innerTool)
{
    private static readonly HashSet<string> AllowedArguments = new(StringComparer.Ordinal)
    {
        "projectPath",
        "startSelector",
        "depth",
        "pageSize",
        "cursor",
    };

    public override ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        return HasValidShape(request.Params.Arguments)
            ? base.InvokeAsync(request, cancellationToken)
            : ValueTask.FromResult(ValidationFailure());
    }

    private static bool HasValidShape(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null)
        {
            return true;
        }

        if (arguments.Keys.Any(key => !AllowedArguments.Contains(key))
            || !IsOptionalString(arguments, "projectPath")
            || !IsOptionalString(arguments, "cursor")
            || !IsOptionalInteger(arguments, "depth")
            || !IsOptionalInteger(arguments, "pageSize"))
        {
            return false;
        }

        if (!arguments.TryGetValue("startSelector", out var selector)
            || selector.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (selector.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var segment in selector.EnumerateArray())
        {
            if (segment.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var properties = segment.EnumerateObject().ToArray();
            if (properties.Length != 2
                || !segment.TryGetProperty("nodeType", out var nodeType)
                || nodeType.ValueKind != JsonValueKind.String
                || !segment.TryGetProperty("name", out var name)
                || name.ValueKind != JsonValueKind.String)
            {
                return false;
            }
        }

        return true;
    }

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

    private static CallToolResult ValidationFailure()
    {
        var response = new BrowseProjectTreeResponse(
            ProjectTreeContract.Version,
            ProjectTreeStatuses.Failed,
            Result: null,
            new BrowseProjectTreeFailure(
                WorkerFailureCategories.ValidationError,
                "The browse_project_tree arguments did not match the declared input schema."),
            Array.Empty<string>());
        return StructuredToolResult.CreateCanonical(
            CanonicalJson.Serialize(response),
            isError: true);
    }
}
