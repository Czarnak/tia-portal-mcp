namespace TiaMcpServer.Contracts;

public sealed class ProjectTreeBrowseResultInfo
{
    public List<ProjectTreeSelectorSegment>? StartSelector { get; set; }

    public int? Depth { get; set; }

    public List<ProjectTreeNode> Roots { get; set; } = new();

    public List<ProjectTreeSkippedNodeInfo> Skipped { get; set; } = new();
}
