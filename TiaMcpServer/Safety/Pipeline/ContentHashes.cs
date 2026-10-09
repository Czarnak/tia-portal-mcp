using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

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

    /// <summary>Returns <c>&lt;format&gt;:sha256:&lt;lower-case hex&gt;</c>; see <see cref="ContentHashRules.Compute"/>.</summary>
    public static string Compute(string format, string content) => ContentHashRules.Compute(format, content);

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

        if (!ContentHashRules.TryParse(expected, out var expectedFormat, out _))
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
    internal static string Sha256Hex(string text) => ContentHashRules.Sha256Hex(text);

    private static ContentHashCheck Reject(string message)
        => new(false, WorkerFailureCategories.ValidationError, message, null);
}
