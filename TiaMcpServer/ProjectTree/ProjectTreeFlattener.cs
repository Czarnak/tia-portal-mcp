using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.ProjectTree;

/// <summary>
/// Projects an already selected and depth-limited tree into one stable pre-order sequence.
/// </summary>
internal static class ProjectTreeFlattener
{
    internal static IReadOnlyList<ProjectTreeFlatNode> Flatten(
        IReadOnlyList<ProjectTreeNode> roots)
    {
        var destination = new List<ProjectTreeFlatNode>();
        foreach (var root in roots)
        {
            Append(root, parentNodeId: null, destination);
        }

        return destination;
    }

    private static void Append(
        ProjectTreeNode source,
        string? parentNodeId,
        List<ProjectTreeFlatNode> destination)
    {
        var sequence = destination.Count;
        var nodeId = $"n{sequence}";
        destination.Add(new ProjectTreeFlatNode(
            nodeId,
            parentNodeId,
            sequence,
            source.Name,
            source.NodeType,
            new Dictionary<string, string>(source.Details ?? new Dictionary<string, string>())));

        foreach (var child in source.Children ?? Enumerable.Empty<ProjectTreeNode>())
        {
            Append(child, nodeId, destination);
        }
    }
}
