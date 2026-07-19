namespace TiaMcpServer.Contracts;

public static class ArchiveModeNames
{
    public const string None = "None";
    public const string DiscardRestorableData = "DiscardRestorableData";
    public const string Compressed = "Compressed";
    public const string DiscardRestorableDataAndCompressed = "DiscardRestorableDataAndCompressed";

    public static bool TryNormalize(string? value, out string normalized, out string? error)
    {
        if (value is null || string.IsNullOrWhiteSpace(value))
        {
            normalized = Compressed;
            error = null;
            return true;
        }

        var trimmed = value.Trim();
        foreach (var candidate in new[]
                 {
                     None,
                     DiscardRestorableData,
                     Compressed,
                     DiscardRestorableDataAndCompressed
                 })
        {
            if (string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                normalized = candidate;
                error = null;
                return true;
            }
        }

        normalized = Compressed;
        error = $"Invalid archive mode '{value}'. Use None, DiscardRestorableData, Compressed, or DiscardRestorableDataAndCompressed.";
        return false;
    }
}
