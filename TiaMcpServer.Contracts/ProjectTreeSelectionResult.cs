namespace TiaMcpServer.Contracts;

public sealed record ProjectTreeSelectionResult(
    List<ProjectTreeNode> Roots,
    IReadOnlyList<ProjectTreeSelectorSegment>? CanonicalStartSelector);
