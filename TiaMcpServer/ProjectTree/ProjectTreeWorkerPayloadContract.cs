using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Worker;

namespace TiaMcpServer.ProjectTree;

internal sealed record ProjectTreeObservation(
    string ResolvedProjectPath,
    IReadOnlyList<ProjectTreeSelectorSegment>? CanonicalStartSelector,
    int? Depth,
    IReadOnlyList<ProjectTreeNode> Roots,
    IReadOnlyList<ProjectTreeSkippedNodeInfo> Skipped,
    IReadOnlyList<string> Warnings);

internal sealed class ProjectTreeProtocolException : Exception
{
    public ProjectTreeProtocolException(string category, string message)
        : base(message)
    {
        Category = category;
    }

    public string Category { get; }
}

/// <summary>
/// The only host-side decoder of a successful project-tree worker snapshot. Rejects payloads that
/// do not exactly match the typed worker contract so stale worker data never reaches pagination.
/// </summary>
internal static class ProjectTreeWorkerPayloadContract
{
    private const string RejectionMessage =
        "The project-tree worker payload did not match its declared result contract and was rejected.";

    internal static ProjectTreeObservation Decode(
        WorkerCallResult workerResult,
        IReadOnlyList<ProjectTreeSelectorSegment>? requestedSelector,
        int? requestedDepth)
    {
        if (!workerResult.Success)
        {
            throw new InvalidOperationException(
                "Only successful worker results may enter the project-tree payload decoder.");
        }

        try
        {
            var payload = CanonicalJson.DeserializeWorkerPayload<ProjectTreeBrowseResultInfo>(
                workerResult.Payload);
            Validate(payload, requestedSelector, requestedDepth);
            var path = ProjectPathNormalization.Canonicalize(workerResult.ResolvedProjectPath)
                ?? throw new JsonException("The worker did not report a canonical resolved project path.");
            return new ProjectTreeObservation(
                path,
                CopySelector(payload.StartSelector),
                payload.Depth,
                payload.Roots,
                payload.Skipped,
                workerResult.Warnings.ToArray());
        }
        catch (Exception exception) when (exception is JsonException or ProjectTreeSelectionException)
        {
            throw new ProjectTreeProtocolException(
                WorkerFailureCategories.ProtocolError,
                RejectionMessage);
        }
    }

    private static void Validate(
        ProjectTreeBrowseResultInfo payload,
        IReadOnlyList<ProjectTreeSelectorSegment>? requestedSelector,
        int? requestedDepth)
    {
        if (payload.Roots is null)
        {
            throw new JsonException("'roots' is declared non-nullable but the payload was null.");
        }

        if (payload.Skipped is null)
        {
            throw new JsonException("'skipped' is declared non-nullable but the payload was null.");
        }

        foreach (var skippedNode in payload.Skipped)
        {
            if (skippedNode is null
                || skippedNode.ParentPath is null
                || skippedNode.Reason is null
                || !IsKnownNodeType(skippedNode.NodeType))
            {
                throw new JsonException("The project-tree payload contained an invalid skipped node.");
            }

            if (skippedNode.ParentPath.Count > 0)
            {
                ProjectTreeNodeTypes.Validate(skippedNode.ParentPath);
            }
        }

        ProjectTreeNodeTypes.Validate(payload.StartSelector);
        ProjectTreeNodeTypes.Validate(requestedSelector);

        if (payload.Depth != requestedDepth)
        {
            throw new JsonException("The worker-reported depth did not match the requested depth.");
        }

        ValidateEquivalentSelector(payload.StartSelector, requestedSelector);
        foreach (var root in payload.Roots)
        {
            ValidateNode(root);
        }
    }

    private static void ValidateEquivalentSelector(
        IReadOnlyList<ProjectTreeSelectorSegment>? observed,
        IReadOnlyList<ProjectTreeSelectorSegment>? requested)
    {
        if (observed is null || requested is null)
        {
            if (observed is not null || requested is not null)
            {
                throw new JsonException(
                    "The worker-reported selector was not semantically equivalent to the request.");
            }

            return;
        }

        if (observed.Count != requested.Count)
        {
            throw new JsonException(
                "The worker-reported selector was not semantically equivalent to the request.");
        }

        for (var index = 0; index < observed.Count; index++)
        {
            if (!string.Equals(observed[index].NodeType, requested[index].NodeType, StringComparison.Ordinal)
                || !string.Equals(observed[index].Name, requested[index].Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new JsonException(
                    "The worker-reported selector was not semantically equivalent to the request.");
            }
        }
    }

    private static void ValidateNode(ProjectTreeNode? node)
    {
        if (node is null)
        {
            throw new JsonException("The project-tree payload contained a null node.");
        }

        if (string.IsNullOrWhiteSpace(node.Name) || !IsKnownNodeType(node.NodeType))
        {
            throw new JsonException("The project-tree payload contained an invalid node.");
        }

        if (node.Details is not null)
        {
            foreach (var detail in node.Details)
            {
                if (string.Equals(detail.Key, "Path", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JsonException("The project-tree payload contained a removed Path detail.");
                }

                if (detail.Value is null)
                {
                    throw new JsonException("The project-tree payload contained a non-string detail value.");
                }
            }
        }

        if (node.Children is null)
        {
            throw new JsonException("The project-tree payload contained null children.");
        }

        foreach (var child in node.Children)
        {
            ValidateNode(child);
        }
    }

    private static bool IsKnownNodeType(string nodeType)
    {
        foreach (var candidate in ProjectTreeNodeTypes.All)
        {
            if (string.Equals(candidate, nodeType, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<ProjectTreeSelectorSegment>? CopySelector(
        IReadOnlyList<ProjectTreeSelectorSegment>? selector)
        => selector?.Select(segment => new ProjectTreeSelectorSegment
        {
            NodeType = segment.NodeType,
            Name = segment.Name
        }).ToArray();
}
