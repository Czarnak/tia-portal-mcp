using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools;

[McpServerToolType]
public class ProjectBindingTools
{
    [McpServerTool(Name = "bind_project", ReadOnly = false, Destructive = false, Idempotent = true,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(BindProjectResponse))]
    [Description("Bind this MCP session to a project already open in a running TIA Portal. This tool never opens, creates, closes or saves a project. Without a path, it binds the only open project or lists the candidates. Re-attaching to another TIA Portal can show TIA's Openness access dialog, which a human must answer.")]
    public static async Task<CallToolResult> BindProject(
        OpennessWorkerClient workerClient,
        [Description("Optional absolute path to a .ap21 project file advertised as open by a running TIA Portal.")] string? projectPath = null,
        [Description("Allow binding a different project than the session's current, configured or last bound project.")] bool forceRebind = false,
        CancellationToken cancellationToken = default)
    {
        var outcome = await workerClient.BindOpenProjectAsync(projectPath, forceRebind, cancellationToken).ConfigureAwait(false);
        var failure = outcome.Failure is null ? null : new StructuredOperationFailure(
            outcome.Failure.FailureCategory ?? WorkerFailureCategories.ProtocolError,
            outcome.Failure.Error ?? "Project binding failed.");
        if (outcome.IsRejection)
            return StructuredStandaloneResult.Create<ProjectBindingResult>("bind_project", null, outcome.Warnings, failure);

        static ProjectBindingInfo BindingInfo(ProjectBindingSnapshot snapshot)
            => new(snapshot.State, snapshot.ProjectPath, snapshot.PortalProcessId);

        var value = new ProjectBindingResult(outcome.Transition, BindingInfo(outcome.After), BindingInfo(outcome.Before),
            outcome.Project, outcome.Portals.Select(portal => new PortalProcessInfo(portal.ProcessId, portal.ProjectPath,
                portal.HasUserInterface, outcome.After.IsVerified && outcome.After.PortalProcessId == portal.ProcessId)).ToArray());
        return StructuredStandaloneResult.Create("bind_project",
            new StandaloneToolOutcome<ProjectBindingResult>(failure is null ? OperationBatchStatus.Succeeded : OperationBatchStatus.Failed,
                value, failure, null), outcome.Warnings);
    }
}

internal static class ProjectBindingToolRegistration
{
    internal static IMcpServerBuilder WithProjectBindingTools(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var method = typeof(ProjectBindingTools).GetMethod(nameof(ProjectBindingTools.BindProject), BindingFlags.Public | BindingFlags.Static)!;
        builder.Services.AddSingleton<McpServerTool>(services => new BindProjectArgumentValidatingTool(McpServerTool.Create(
            method, target: null, options: new McpServerToolCreateOptions
            {
                Services = services,
                SchemaCreateOptions = new AIJsonSchemaCreateOptions
                {
                    TransformSchemaNode = AllowNullForceRebind,
                    TransformOptions = new AIJsonSchemaTransformOptions { DisallowAdditionalProperties = true }
                }
            })));
        return builder;
    }

    private static JsonNode AllowNullForceRebind(AIJsonSchemaCreateContext context, JsonNode schema)
    {
        // The SDK generates each method parameter as a root schema; this tool has one bool input.
        // Response bool properties keep their non-nullable schema.
        if (context.TypeInfo.Type == typeof(bool) && context.PropertyInfo is null
            && schema is JsonObject forceSchema)
            forceSchema["type"] = new JsonArray("boolean", "null");
        return schema;
    }
}

internal sealed class BindProjectArgumentValidatingTool(McpServerTool innerTool) : DelegatingMcpServerTool(innerTool)
{
    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        var arguments = request.Params.Arguments;
        if (!ValidArguments(arguments))
            return ValueTask.FromResult(StructuredStandaloneResult.Create<ProjectBindingResult>("bind_project", null,
                Array.Empty<string>(), new(WorkerFailureCategories.ValidationError,
                    "bind_project requires only an optional absolute .ap21 projectPath and an optional boolean forceRebind.")));

        // JSON null means the optional argument was omitted; the SDK's CLR bool receives its default.
        if (arguments is not null && arguments.TryGetValue("forceRebind", out var force) && force.ValueKind == JsonValueKind.Null)
        {
            request.Params.Arguments = new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal);
            request.Params.Arguments.Remove("forceRebind");
        }
        return base.InvokeAsync(request, cancellationToken);
    }

    private static bool ValidArguments(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null) return true;
        if (arguments.Keys.Any(key => key is not ("projectPath" or "forceRebind"))) return false;
        if (arguments.TryGetValue("forceRebind", out var force)
            && force.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)) return false;
        if (!arguments.TryGetValue("projectPath", out var path) || path.ValueKind == JsonValueKind.Null) return true;
        if (path.ValueKind != JsonValueKind.String) return false;
        var text = path.GetString()!;
        return Path.IsPathFullyQualified(text) && string.Equals(Path.GetExtension(text), ".ap21", StringComparison.OrdinalIgnoreCase);
    }
}
