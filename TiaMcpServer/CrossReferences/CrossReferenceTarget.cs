using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.CrossReferences;

/// <summary>
/// The caller-facing cross-reference target: a <c>browse_project_tree</c> selector path, optionally
/// narrowed to one tag or constant under a <c>TagTable</c> path. Mapped one-to-one to
/// <see cref="CrossReferenceSelectorInfo"/>, the host-to-worker selector boundary.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CrossReferenceTargetSelector
{
    [JsonRequired]
    [Description("Ordered { nodeType, name } segments copied from browse_project_tree output, starting at a Device. Names match case-insensitively, node types exactly. A path ending at a block or type reads that object; a path ending at a PLC, software unit or folder sweeps every owner beneath it.")]
    public List<ProjectTreeSelectorSegment> Path { get; set; } = new();

    [Description("Optional tag or constant inside the TagTable the path ends at. Omit to read the whole table.")]
    public CrossReferenceMemberSelector? Member { get; set; }

    internal CrossReferenceSelectorInfo ToInfo() => new()
    {
        Path = Path.ToList(),
        Member = Member is null ? null : new CrossReferenceMemberSelectorInfo { Kind = Member.Kind, Name = Member.Name },
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CrossReferenceMemberSelector
{
    [JsonRequired]
    [Description("Exactly one of Tag, SystemConstant, UserConstant (exact case). Every kind lives in a TagTable, so target.path must end at a TagTable segment.")]
    public string Kind { get; set; } = string.Empty;

    [JsonRequired]
    [Description("Name of the tag or constant inside the table. Matches case-insensitively.")]
    public string Name { get; set; } = string.Empty;
}
