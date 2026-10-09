namespace TiaMcpServer.Contracts.Project;

public sealed record ProjectTreeSelectionResult(
    List<ProjectTreeNode> Roots,
    IReadOnlyList<ProjectTreeSelectorSegment>? CanonicalStartSelector,
    List<ProjectTreeSkippedNodeInfo> Skipped);
