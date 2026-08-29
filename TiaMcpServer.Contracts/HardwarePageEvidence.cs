using System.Security.Cryptography;
using System.Text;

namespace TiaMcpServer.Contracts;

/// <summary>Deterministic query evidence for hardware-config pagination continuations.</summary>
public static class HardwarePageEvidence
{
    /// <summary>
    /// Creates a SHA-256 hash over the stable query fields in fixed order. A missing Boolean is
    /// equivalent to false; device names use the same ordinal-ignore-case semantics as the reader,
    /// while PLC names remain exact ordinal values. Page size is intentionally not a query field.
    /// </summary>
    public static string CreateQueryHash(
        string? deviceName,
        string? plcName,
        bool? includeIoDetails,
        bool? includeTagMatches)
    {
        var canonical = string.Concat(
            "deviceName=", NormalizeOrdinalIgnoreCase(deviceName), "\n",
            "plcName=", plcName ?? string.Empty, "\n",
            "includeIoDetails=", (includeIoDetails ?? false) ? "true" : "false", "\n",
            "includeTagMatches=", (includeTagMatches ?? false) ? "true" : "false", "\n");
        return ComputeSha256(canonical);
    }

    /// <summary>Creates deterministic snapshot evidence from a fixed canonical representation.</summary>
    public static string CreateSnapshotHash(string canonicalSnapshot)
        => ComputeSha256(canonicalSnapshot ?? throw new ArgumentNullException(nameof(canonicalSnapshot)));

    private static string NormalizeOrdinalIgnoreCase(string? value)
        => value?.ToUpperInvariant() ?? string.Empty;

    private static string ComputeSha256(string value)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = sha256.ComputeHash(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var valueByte in hash)
        {
            builder.Append(valueByte.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
