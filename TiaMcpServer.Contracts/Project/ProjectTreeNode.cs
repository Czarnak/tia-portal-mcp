namespace TiaMcpServer.Contracts;

public class ProjectTreeNode
{
    public string Name { get; set; } = string.Empty;

    public string NodeType { get; set; } = string.Empty;

    public Dictionary<string, string>? Details { get; set; }

    public List<ProjectTreeNode>? Children { get; set; }
}
