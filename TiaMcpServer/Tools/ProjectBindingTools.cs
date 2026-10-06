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
        OpenWorld = true, UseStructuredContent = true, OutputSchemaType = typeof(BindProjectResponse))]
    [Description("Bind this MCP session to a project already open in a running TIA Portal, or explicitly inspect Portal and Project Server inventory. This tool never opens, creates, closes or saves a project. The default bind action without a path binds the only open project or lists the candidates. Inspection actions never adopt or switch a project. Re-attaching to another TIA Portal can show TIA's Openness access dialog, which a human must answer.")]
    public static async Task<CallToolResult> BindProject(
        OpennessWorkerClient workerClient,
        [Description("Optional absolute path to a .ap21 project file advertised as open by a running TIA Portal.")] string? projectPath = null,
        [Description("Allow binding a different project than the session's current, configured or last bound project.")] bool forceRebind = false,
        CancellationToken cancellationToken = default,
        [Description("Optional action: bind (default), list_portals, list_server_connections, list_server_groups, list_server_projects, list_local_sessions, get_lock_state. Names are case-sensitive.")] string? action = null,
        [Description("Optional positive Portal PID assertion for inventory; cannot switch an existing attachment.")] int? portalProcessId = null,
        [Description("Exact configured Project Server alias; required after list_server_connections.")] string? serverAlias = null,
        [Description("Exact group: root is {isRoot:true,name:null}; named is {isRoot:false,name:exactName}. Both members are required.")] MultiuserGroupSelector? group = null,
        [Description("Exact server project name, required for list_local_sessions and get_lock_state.")] string? serverProjectName = null)
    {
        action ??= "bind";
        if (action != "bind")
        {
            if (projectPath is not null || forceRebind || !ProjectBindingInspectionCatalog.TryCreate(
                action, portalProcessId, serverAlias, group, serverProjectName, out var request))
                return InvalidArguments();
            var inspected = await workerClient.InspectPortalAsync(request, cancellationToken).ConfigureAwait(false);
            if (!inspected.Result.Success && inspected.Result.DispatchState == WorkerDispatchState.NotSent)
                return StructuredStandaloneResult.Create<ProjectBindingResult>("bind_project", null,
                    inspected.Result.Warnings, StandalonePayloadContract.Failure(inspected.Result));
            var empty = new ProjectBindingInspectionInfo(action, null, null, null, null, null, null);
            var decoded = action == "list_portals"
                ? new StandaloneToolOutcome<ProjectBindingInspectionInfo>(inspected.Result.Success ? OperationBatchStatus.Succeeded : OperationBatchStatus.Failed,
                    inspected.Result.Success ? empty with { PortalProcessId = inspected.Result.PortalProcessId } : null,
                    inspected.Result.Success ? null : StandalonePayloadContract.Failure(inspected.Result), null)
                : ProjectBindingInspectionPayloadContract.Decode(request, inspected.Result);
            var inspectionValue = new ProjectBindingResult(ProjectBindingTransitions.None, BindingInfo(inspected.After), BindingInfo(inspected.Before),
                null, Portals(inspected.Portals, inspected.After), decoded.Value ?? empty);
            return StructuredStandaloneResult.Create("bind_project",
                new StandaloneToolOutcome<ProjectBindingResult>(decoded.Status, inspectionValue, decoded.Failure, null), inspected.Result.Warnings,
                omissionGuidance: "The complete inspection inventory was omitted. Inspect a narrower exact group or project using an applicable inventory action, or inspect the inventory in TIA Portal. Repeating the same oversized inventory may still exceed the limit.");
        }
        if (portalProcessId is not null || serverAlias is not null || group is not null || serverProjectName is not null)
            return InvalidArguments();
        var outcome = await workerClient.BindOpenProjectAsync(projectPath, forceRebind, cancellationToken).ConfigureAwait(false);
        var failure = outcome.Failure is null ? null : new StructuredOperationFailure(
            outcome.Failure.FailureCategory ?? WorkerFailureCategories.ProtocolError,
            outcome.Failure.Error ?? "Project binding failed.");
        if (outcome.IsRejection)
            return StructuredStandaloneResult.Create<ProjectBindingResult>("bind_project", null, outcome.Warnings, failure);

        var value = new ProjectBindingResult(outcome.Transition, BindingInfo(outcome.After), BindingInfo(outcome.Before),
            outcome.Project, Portals(outcome.Portals, outcome.After));
        return StructuredStandaloneResult.Create("bind_project",
            new StandaloneToolOutcome<ProjectBindingResult>(failure is null ? OperationBatchStatus.Succeeded : OperationBatchStatus.Failed,
                value, failure, null), outcome.Warnings);
    }

    private static ProjectBindingInfo BindingInfo(ProjectBindingSnapshot snapshot)
        => new(snapshot.State, snapshot.ProjectPath, snapshot.PortalProcessId);

    private static PortalProcessInfo[] Portals(IReadOnlyList<TiaPortalProcessInfo> portals, ProjectBindingSnapshot binding)
        => portals.Select(portal => new PortalProcessInfo(portal.ProcessId, portal.ProjectPath,
            portal.HasUserInterface, binding.IsVerified && binding.PortalProcessId == portal.ProcessId)).ToArray();

    private static CallToolResult InvalidArguments() => StructuredStandaloneResult.Create<ProjectBindingResult>("bind_project", null,
        Array.Empty<string>(), new(WorkerFailureCategories.ValidationError, ProjectBindingInspectionCatalog.InvalidArguments));
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
        if (!ProjectBindingInspectionCatalog.ValidArguments(arguments))
            return ValueTask.FromResult(StructuredStandaloneResult.Create<ProjectBindingResult>("bind_project", null,
                Array.Empty<string>(), new(WorkerFailureCategories.ValidationError,
                    ProjectBindingInspectionCatalog.InvalidArguments)));

        // JSON null means the optional argument was omitted; the SDK's CLR bool receives its default.
        if (arguments is not null && arguments.TryGetValue("forceRebind", out var force) && force.ValueKind == JsonValueKind.Null)
        {
            request.Params.Arguments = new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal);
            request.Params.Arguments.Remove("forceRebind");
        }
        return base.InvokeAsync(request, cancellationToken);
    }

}
