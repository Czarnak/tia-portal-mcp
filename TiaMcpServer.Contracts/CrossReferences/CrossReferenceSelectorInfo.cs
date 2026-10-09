using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Contracts.CrossReferences;

/// <summary>
/// Cross-reference target: a <c>browse_project_tree</c> selector path, optionally narrowed to a
/// member the tree does not model (a tag or constant under a <c>TagTable</c> path).
/// </summary>
public class CrossReferenceSelectorInfo
{
    public List<ProjectTreeSelectorSegment> Path { get; set; } = new List<ProjectTreeSelectorSegment>();

    public CrossReferenceMemberSelectorInfo? Member { get; set; }
}

public class CrossReferenceMemberSelectorInfo
{
    /// <summary>One of <see cref="CrossReferenceMemberKinds.All"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
