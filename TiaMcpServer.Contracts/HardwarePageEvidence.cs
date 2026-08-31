using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;

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
        var canonical = new List<byte>();
        AppendLengthFramedUtf8(canonical, NormalizeOrdinalIgnoreCase(deviceName));
        AppendLengthFramedUtf8(canonical, plcName ?? string.Empty);
        AppendLengthFramedUtf8(canonical, (includeIoDetails ?? false) ? "true" : "false");
        AppendLengthFramedUtf8(canonical, (includeTagMatches ?? false) ? "true" : "false");
        return ComputeSha256(canonical.ToArray());
    }

    /// <summary>Creates deterministic snapshot evidence from a fixed canonical representation.</summary>
    public static string CreateSnapshotHash(string canonicalSnapshot)
        => ComputeSha256(canonicalSnapshot ?? throw new ArgumentNullException(nameof(canonicalSnapshot)));

    private static string NormalizeOrdinalIgnoreCase(string? value)
        => value?.ToUpperInvariant() ?? string.Empty;

    private static void AppendLengthFramedUtf8(List<byte> destination, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var length = bytes.Length;
        destination.Add((byte)(length >> 24));
        destination.Add((byte)(length >> 16));
        destination.Add((byte)(length >> 8));
        destination.Add((byte)length);
        destination.AddRange(bytes);
    }

    private static string ComputeSha256(string value)
        => ComputeSha256(Encoding.UTF8.GetBytes(value));

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var valueByte in hash)
        {
            builder.Append(valueByte.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
