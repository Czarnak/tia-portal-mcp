using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts;

public sealed class NetworkInterfacePathSegmentInfo
{
    public string Name { get; set; } = string.Empty;
    public int PositionNumber { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TypeIdentifier { get; set; }
}
