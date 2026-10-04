using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts;

/// <summary>One exact name/position pair on the path to an item's NetworkInterface service.</summary>
public sealed class NetworkInterfacePathSegmentInfo
{
    public string Name { get; set; } = string.Empty;
    public int PositionNumber { get; set; }
    /// <summary>Extra consistency evidence when the optional type identifier is readable.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TypeIdentifier { get; set; }
}
