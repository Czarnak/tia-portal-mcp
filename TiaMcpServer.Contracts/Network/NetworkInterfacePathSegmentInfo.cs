using System.Text.Json.Serialization;
using System.Text.Json;

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

/// <summary>Canonical owner-path text: validates a path and compares two paths exactly. Never a wire value.</summary>
public static class NetworkInterfacePathEncoding
{
    public static string Encode(IReadOnlyList<NetworkInterfacePathSegmentInfo> path)
    {
        if (path is null || path.Count == 0) throw new JsonException("Interface owner path must be nonempty.");
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var segment in path)
            {
                if (segment is null || string.IsNullOrWhiteSpace(segment.Name) || segment.PositionNumber < 0
                    || (segment.TypeIdentifier is not null && string.IsNullOrWhiteSpace(segment.TypeIdentifier)))
                    throw new JsonException("Invalid interface owner segment.");
                writer.WriteStartObject(); writer.WriteString("name", segment.Name);
                writer.WriteNumber("positionNumber", segment.PositionNumber);
                if (segment.TypeIdentifier is not null) writer.WriteString("typeIdentifier", segment.TypeIdentifier);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
