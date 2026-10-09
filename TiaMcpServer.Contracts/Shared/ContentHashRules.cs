using System.Security.Cryptography;
using System.Text;
using TiaMcpServer.Contracts.Block;

namespace TiaMcpServer.Contracts.Shared;

/// <summary>
/// Format-tagged content hashes (<c>xml:sha256:&lt;hex&gt;</c> / <c>source:sha256:&lt;hex&gt;</c>) shared by
/// host and worker for optimistic-concurrency checks over the served document text.
/// </summary>
public static class ContentHashRules
{
    private const string Algorithm = "sha256";
    private const int HexLength = 64;

    /// <summary>Returns <c>&lt;format&gt;:sha256:&lt;lower-case hex&gt;</c> over the UTF-8 bytes of <paramref name="content"/>.</summary>
    public static string Compute(string format, string content)
    {
        if (!SourceFormatNames.Allowed.Contains(format))
        {
            throw new ArgumentException(
                $"Unknown format '{format}'. Allowed values: {string.Join(", ", SourceFormatNames.Allowed)}.",
                nameof(format));
        }

        return format + ":" + Algorithm + ":" + Sha256Hex(content);
    }

    /// <summary>Lower-case hexadecimal SHA-256 of the UTF-8 encoding of <paramref name="text"/>.</summary>
    public static string Sha256Hex(string text)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            builder.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Parses <c>&lt;format&gt;:sha256:&lt;64 lower-case hex&gt;</c> with a format from
    /// <see cref="SourceFormatNames.Allowed"/>.
    /// </summary>
    public static bool TryParse(string? hash, out string format, out string hex)
    {
        format = string.Empty;
        hex = string.Empty;
        if (hash is null)
        {
            return false;
        }

        var parts = hash.Split(':');
        if (parts.Length != 3
            || !SourceFormatNames.Allowed.Contains(parts[0])
            || !string.Equals(parts[1], Algorithm, StringComparison.Ordinal)
            || parts[2].Length != HexLength
            || !parts[2].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            return false;
        }

        format = parts[0];
        hex = parts[2];
        return true;
    }
}
