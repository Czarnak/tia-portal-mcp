using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.CrossReferences;

[McpServerToolType]
public class CrossReferenceReadTools
{
    private const string ToolName = "read_cross_references";
    private const string UnusedWarning = "An incomplete UnusedObjects result is not proof that objects can be deleted.";

    [McpServerTool(
        Name = ToolName,
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ReadCrossReferencesResponse))]
    [Description("Read cross-references of one project-tree target: a block, type, tag, constant, or a PLC, software unit or folder swept over every owner beneath it. Returns sources with their referenced objects and access locations; isComplete is false when an owner failed or the result was cut, with messages explaining why. An unsupported target kind is a typed failure, not an empty result.")]
    public static async Task<CallToolResult> ReadCrossReferences(
        OpennessWorkerClient workerClient,
        [Description("The target: { path: [{ nodeType, name }, ...], member?: { kind, name } }. Copy path segments from browse_project_tree output.")] CrossReferenceTargetSelector target,
        [Description("Optional filter: AllObjects, ObjectsWithReferences (default), ObjectsWithoutReferences, or UnusedObjects.")] string? filter = null,
        [Description("Optional cap on returned sources, 1 or greater. When cut, isComplete is false.")] int? maxResults = null,
        [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
    {
        var invalid = Validate(target, filter, maxResults);
        if (invalid is not null)
            return StructuredStandaloneResult.Create<CrossReferenceReport>(ToolName, null, Array.Empty<string>(), invalid);

        var result = await workerClient.ReadCrossReferencesAsync(projectPath, target.ToInfo(), filter, maxResults)
            .ConfigureAwait(false);
        var rejection = StandalonePayloadContract.Rejection(result);
        var outcome = rejection is null ? CrossReferencePayloadContract.Project(result) : null;

        var warnings = result.Warnings.ToList();
        CrossReferenceFilterNames.TryNormalize(filter, out var normalized, out _);
        if (outcome?.Value is { IsComplete: false } && normalized == CrossReferenceFilterNames.UnusedObjects)
            warnings.Add(UnusedWarning);
        return StructuredStandaloneResult.Create(ToolName, outcome, warnings, rejection);
    }

    private static StructuredOperationFailure? Validate(CrossReferenceTargetSelector? target, string? filter, int? maxResults)
    {
        if (target?.Path is null || target.Path.Count == 0)
            return Fail(WorkerFailureCategories.InvalidSelector, "target.path must contain at least one segment.");
        try
        {
            ProjectTreeNodeTypes.Validate(target.Path);
        }
        catch (ProjectTreeSelectionException exception)
        {
            return Fail(exception.Category, exception.Message.Replace("startSelector", "target.path", StringComparison.Ordinal));
        }

        if (target.Member is { } member)
        {
            if (!CrossReferenceMemberKinds.All.Contains(member.Kind))
                return Fail(WorkerFailureCategories.InvalidSelector,
                    $"target.member.kind must be one of {string.Join(", ", CrossReferenceMemberKinds.All)} (exact case).");
            if (string.IsNullOrWhiteSpace(member.Name))
                return Fail(WorkerFailureCategories.InvalidSelector, "target.member.name must not be blank.");
            // Every member kind is owned by a TagTable (spec Appendix B).
            if (target.Path[^1].NodeType != ProjectTreeNodeTypes.TagTable)
                return Fail(WorkerFailureCategories.InvalidSelector,
                    $"A {member.Kind} member needs target.path to end at a {ProjectTreeNodeTypes.TagTable} segment.");
        }

        if (!CrossReferenceFilterNames.TryNormalize(filter, out _, out var filterError))
            return Fail(WorkerFailureCategories.ValidationError, filterError!);
        if (maxResults < 1)
            return Fail(WorkerFailureCategories.ValidationError, "maxResults must be 1 or greater.");
        return null;
    }

    private static StructuredOperationFailure Fail(string category, string message) => new(category, message);
}
