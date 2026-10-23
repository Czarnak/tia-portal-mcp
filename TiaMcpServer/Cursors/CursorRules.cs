using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Cursors;

/// <summary>A cursor rejected by a codec; <see cref="Category"/> is the worker failure category.</summary>
internal sealed class CursorException(string category, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    internal string Category { get; } = category;
}

/// <summary>Per-domain cursor identity: protector purpose, client message and foreign-process category.</summary>
internal sealed record CursorKind(string Purpose, string InvalidMessage, string ForeignProcessCategory);

/// <summary>Validation rules shared by every host cursor state.</summary>
internal static class CursorStateRules
{
    internal const int MaxSnapshotIdChars = 128;

    // No regex: "$" would let a trailing "\n" through.
    internal static bool IsLowerSha256(string? value)
        => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool IsValidSnapshotId(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= MaxSnapshotIdChars;

    internal static bool SameProject(string? first, string? second)
    {
        var normalizedFirst = ProjectPathNormalization.Canonicalize(first);
        var normalizedSecond = ProjectPathNormalization.Canonicalize(second);
        return normalizedFirst is not null
            && normalizedSecond is not null
            && string.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase);
    }
}
