namespace TiaMcpServer.Contracts;

/// <summary>
/// A node the worker could not read while walking the project tree. <see cref="ParentPath"/> is the
/// typed selector of the node's parent; <see cref="NodeType"/> is the type the node would have had.
/// Internal evidence for write planning; browse output never shows it.
/// </summary>
public sealed class ProjectTreeSkippedNodeInfo
{
    public List<ProjectTreeSelectorSegment> ParentPath { get; set; } = new();

    public string NodeType { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}
