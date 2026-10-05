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

/// <summary>Canonical typed owner identity for scalar verification dictionaries.</summary>
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

    public static IReadOnlyList<NetworkInterfacePathSegmentInfo> Decode(string encoded)
    {
        using var document = JsonDocument.Parse(encoded);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Interface path must be an array.");
        var path = new List<NetworkInterfacePathSegmentInfo>();
        foreach (var value in document.RootElement.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object) throw new JsonException("Interface segment must be an object.");
            var fields = new HashSet<string>(StringComparer.Ordinal);
            var segment = new NetworkInterfacePathSegmentInfo();
            foreach (var field in value.EnumerateObject())
            {
                if (!fields.Add(field.Name)) throw new JsonException("Duplicate interface segment field.");
                switch (field.Name)
                {
                    case "name":
                        if (field.Value.ValueKind != JsonValueKind.String) throw new JsonException("Invalid name.");
                        segment.Name = field.Value.GetString()!; break;
                    case "positionNumber":
                        if (field.Value.ValueKind != JsonValueKind.Number || !field.Value.TryGetInt32(out var position)) throw new JsonException("Invalid positionNumber.");
                        segment.PositionNumber = position; break;
                    case "typeIdentifier":
                        if (field.Value.ValueKind != JsonValueKind.String) throw new JsonException("Invalid typeIdentifier.");
                        segment.TypeIdentifier = field.Value.GetString(); break;
                    default: throw new JsonException("Unknown interface segment field.");
                }
            }
            if (!fields.Contains("name") || !fields.Contains("positionNumber")) throw new JsonException("Required interface segment field missing.");
            path.Add(segment);
        }
        _ = Encode(path); // Validate semantic requirements using the same typed writer.
        return path;
    }
}
