using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ProjectTreeSelectorSegment
{
    [JsonRequired]
    public string NodeType { get; set; } = string.Empty;

    [JsonRequired]
    public string Name { get; set; } = string.Empty;
}
