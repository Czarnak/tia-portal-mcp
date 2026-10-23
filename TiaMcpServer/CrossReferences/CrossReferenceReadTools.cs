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
    [Description("Read cross-references of one project-tree target in the bound project: a block, type, tag, constant, or a PLC, software unit or folder swept over every owner beneath it. Provisional: target { hmi: { hmiName, tagName } } instead of path reads one top-level WinCC Unified HMI tag (tags only, hmiName required; may be replaced by a unified HMI selector). Returns sources with their referenced objects and access locations; isComplete is false when an owner failed or the result was cut, with messages explaining why. An unsupported target kind is a typed failure, not an empty result.")]
    public static async Task<CallToolResult> ReadCrossReferences(
        OpennessWorkerClient workerClient,
        [Description("The target: { path: [{ nodeType, name }, ...], member?: { kind, name } } or, provisionally, { hmi: { hmiName, tagName } }, never both. Copy path segments from browse_project_tree output.")] CrossReferenceTargetSelector target,
        [Description("Optional filter: AllObjects, ObjectsWithReferences (default), ObjectsWithoutReferences, or UnusedObjects.")] string? filter = null,
        [Description("Optional cap on returned sources, 1 or greater. When cut, isComplete is false.")] int? maxResults = null)
    {
        var invalid = Validate(target, filter, maxResults);
        if (invalid is not null)
            return StructuredStandaloneResult.Create<CrossReferenceReport>(ToolName, null, Array.Empty<string>(), invalid);

        var result = await workerClient.ReadCrossReferencesAsync(selector: target.ToInfo(), filter: filter, maxResults: maxResults)
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
        var hasPath = target?.Path is { Count: > 0 };
        if (target?.Hmi is { } hmi)
        {
            if (hasPath || target.Member is not null)
                return Fail(WorkerFailureCategories.ValidationError, "target takes either path (with optional member) or hmi, not both.");
            if (string.IsNullOrWhiteSpace(hmi.HmiName))
                return Fail(WorkerFailureCategories.ValidationError, "target.hmi.hmiName is required and must not be blank.");
            if (string.IsNullOrWhiteSpace(hmi.TagName))
                return Fail(WorkerFailureCategories.ValidationError, "target.hmi.tagName must not be blank.");
            return ValidateOptions(filter, maxResults);
        }

        if (target?.Path is not { Count: > 0 })
            return Fail(WorkerFailureCategories.ValidationError, "target needs either a non-empty path or hmi.");
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

        return ValidateOptions(filter, maxResults);
    }

    private static StructuredOperationFailure? ValidateOptions(string? filter, int? maxResults)
    {
        if (!CrossReferenceFilterNames.TryNormalize(filter, out _, out var filterError))
            return Fail(WorkerFailureCategories.ValidationError, filterError!);
        if (maxResults < 1)
            return Fail(WorkerFailureCategories.ValidationError, "maxResults must be 1 or greater.");
        return null;
    }

    private static StructuredOperationFailure Fail(string category, string message) => new(category, message);
}
