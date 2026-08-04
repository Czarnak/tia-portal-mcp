using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker;

internal sealed class NetworkObjectCursorPayload
{
    public int Version { get; set; } = 1;
    public int Offset { get; set; }
    public string QueryHash { get; set; } = string.Empty;
    public string SnapshotHash { get; set; } = string.Empty;
}

internal sealed class NetworkCursorException : Exception
{
    public NetworkCursorException(string category)
        : base("The supplied network object cursor is not valid for this request.")
    {
        Category = category;
    }

    public string Category { get; }
}

public static class NetworkObjectCursorCodec
{
    private const string OrderingVersion = "network-object-order-v1";
    private static readonly Regex LowercaseSha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string CreateQueryHash(IReadOnlyList<string> objectKinds, string? deviceName)
    {
        var normalizedKinds = objectKinds.OrderBy(kind => kind, StringComparer.Ordinal).ToArray();
        var document = JsonSerializer.Serialize(new CursorQuery
        {
            OrderingVersion = OrderingVersion,
            ObjectKinds = normalizedKinds,
            DeviceName = deviceName,
        }, JsonOptions);
        return Hash(document);
    }

    public static string CreateSnapshotHash(IReadOnlyList<NetworkObjectSummaryInfo> orderedItems)
    {
        var evidence = new StringBuilder();
        foreach (var item in orderedItems)
        {
            Append(evidence, item.Kind);
            Append(evidence, item.Selector is not null ? "selectable" : "unselectable");
            var selector = item.Selector;
            if (selector is null)
            {
                continue;
            }

            Append(evidence, selector.Kind);
            Append(evidence, selector.DeviceName);
            Append(evidence, selector.NodeId);
            Append(evidence, selector.SubnetId);
            Append(evidence, selector.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(evidence, selector.InterfaceName);
            Append(evidence, selector.InterfaceType);
            Append(evidence, selector.InterfaceOperatingMode);
            Append(evidence, selector.ConnectionIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(evidence, selector.ConnectionType);
            Append(evidence, selector.LocalConnectionName);
            Append(evidence, selector.LocalConnectionId);
            foreach (var segment in selector.ItemPath ?? Enumerable.Empty<DeviceItemPathSegmentInfo>())
            {
                Append(evidence, segment.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Append(evidence, segment.Name);
                Append(evidence, segment.PositionNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Append(evidence, segment.TypeIdentifier);
            }
        }

        return Hash(evidence.ToString());
    }

    public static string Encode(int offset, string queryHash, string snapshotHash)
    {
        var payload = new NetworkObjectCursorPayload
        {
            Offset = offset,
            QueryHash = queryHash,
            SnapshotHash = snapshotHash,
        };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static NetworkObjectCursorPayload Decode(
        string cursor,
        string expectedQueryHash,
        string expectedSnapshotHash,
        int totalCount)
    {
        NetworkObjectCursorPayload? payload;
        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            payload = JsonSerializer.Deserialize<NetworkObjectCursorPayload>(Encoding.UTF8.GetString(Convert.FromBase64String(normalized)), JsonOptions);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException)
        {
            throw new NetworkCursorException(WorkerFailureCategories.InvalidCursor);
        }

        if (payload is null || payload.Version != 1 || payload.Offset < 0
            || !IsHash(payload.QueryHash) || !IsHash(payload.SnapshotHash))
        {
            throw new NetworkCursorException(WorkerFailureCategories.InvalidCursor);
        }

        if (!string.Equals(payload.QueryHash, expectedQueryHash, StringComparison.Ordinal))
        {
            throw new NetworkCursorException(WorkerFailureCategories.CursorFilterMismatch);
        }

        if (!string.Equals(payload.SnapshotHash, expectedSnapshotHash, StringComparison.Ordinal))
        {
            throw new NetworkCursorException(WorkerFailureCategories.CursorSnapshotMismatch);
        }

        if (payload.Offset > totalCount)
        {
            throw new NetworkCursorException(WorkerFailureCategories.CursorOutOfRange);
        }

        return payload;
    }

    private static bool IsHash(string? value) => value is not null && LowercaseSha256.IsMatch(value);

    private static string Hash(string value)
    {
        using (var algorithm = SHA256.Create())
        {
            return BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(value)))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }
    }

    private static void Append(StringBuilder builder, string? value)
    {
        var text = value ?? string.Empty;
        builder.Append(text.Length).Append(':').Append(text).Append(';');
    }

    private sealed class CursorQuery
    {
        public string OrderingVersion { get; set; } = string.Empty;
        public string[] ObjectKinds { get; set; } = Array.Empty<string>();
        public string? DeviceName { get; set; }
    }
}
