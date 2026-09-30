using System.Security.Cryptography;
using System.Text;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.Safety.Pipeline;

/// <summary>One precondition a guarded write checked, with what was expected and what was found.</summary>
public sealed record CheckedPrecondition(string Name, string Expected, string Actual, bool Satisfied);

/// <summary>
/// Outcome of comparing a caller's expected content hash with freshly read content.
/// <see cref="ErrorCategory"/> is a <see cref="WorkerFailureCategories"/> value when
/// <see cref="Success"/> is false; <see cref="Evidence"/> is present only when a comparison ran.
/// </summary>
public sealed record ContentHashCheck(
    bool Success,
    string? ErrorCategory,
    string? Message,
    CheckedPrecondition? Evidence);

/// <summary>
/// Format-tagged content hashes (<c>xml:sha256:&lt;hex&gt;</c> / <c>source:sha256:&lt;hex&gt;</c>)
/// used for optimistic-concurrency checks over the served document text.
/// </summary>
public static class ContentHashes
{
    /// <summary>Name of the precondition recorded for a hash comparison.</summary>
    public const string PreconditionName = "contentHash";

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

        return $"{format}:{Algorithm}:{Sha256Hex(content)}";
    }

    /// <summary>
    /// Compares <paramref name="expected"/> with the hash of <paramref name="freshContent"/> in
    /// <paramref name="writeFormat"/>. Malformed or cross-format input is a validation error;
    /// a well-formed hash that no longer matches is <c>state_changed</c>.
    /// </summary>
    public static ContentHashCheck Check(string? expected, string writeFormat, string freshContent)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return Reject("expectedContentHash is required (format-tagged, for example 'xml:sha256:<hex>').");
        }

        if (!TryParse(expected, out var expectedFormat))
        {
            return Reject(
                $"expectedContentHash '{expected}' is malformed. Expected '<format>:sha256:<64 lower-case hex>' " +
                $"with format one of: {string.Join(", ", SourceFormatNames.Allowed)}.");
        }

        if (!string.Equals(expectedFormat, writeFormat, StringComparison.Ordinal))
        {
            return Reject(
                $"expectedContentHash was taken from the '{expectedFormat}' format but the write uses the '{writeFormat}' format. " +
                "Read the object in the write format and use that hash.");
        }

        var actual = Compute(writeFormat, freshContent);
        var satisfied = string.Equals(expected, actual, StringComparison.Ordinal);
        var evidence = new CheckedPrecondition(PreconditionName, expected, actual, satisfied);

        return satisfied
            ? new ContentHashCheck(true, null, null, evidence)
            : new ContentHashCheck(
                false,
                WorkerFailureCategories.StateChanged,
                "The content changed since it was read: the current hash no longer matches expectedContentHash. Re-read and retry.",
                evidence);
    }

    /// <summary>Lower-case hexadecimal SHA-256 of the UTF-8 encoding of <paramref name="text"/>.</summary>
    internal static string Sha256Hex(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static ContentHashCheck Reject(string message)
        => new(false, WorkerFailureCategories.ValidationError, message, null);

    private static bool TryParse(string value, out string format)
    {
        format = string.Empty;
        var parts = value.Split(':');
        if (parts.Length != 3
            || !SourceFormatNames.Allowed.Contains(parts[0])
            || !string.Equals(parts[1], Algorithm, StringComparison.Ordinal)
            || parts[2].Length != HexLength
            || !parts[2].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            return false;
        }

        format = parts[0];
        return true;
    }
}
