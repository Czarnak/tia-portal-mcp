namespace TiaMcpServer.Contracts;

public static class ArchiveModeNames
{
    public const string None = "None";
    public const string DiscardRestorableData = "DiscardRestorableData";
    public const string Compressed = "Compressed";
    public const string DiscardRestorableDataAndCompressed = "DiscardRestorableDataAndCompressed";

    /// <summary>
    /// TIA Portal V21's single-file archive extension. Siemens.Engineering.Project.Archive does not
    /// append this itself even when the target name omits it - confirmed against a live TIA Portal
    /// V21 instance, where a Compressed-mode archive was written with no file extension at all.
    /// </summary>
    public const string CompressedFileExtension = ".zap21";

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

    /// <summary>
    /// Only Compressed and DiscardRestorableDataAndCompressed produce a single retrievable archive
    /// file; None and DiscardRestorableData write a project folder instead, which has no extension
    /// to add. Idempotent: an already-correct extension (any casing) is left alone. Callers must
    /// pass a non-blank <paramref name="archiveName"/> - this method does not validate it.
    /// </summary>
    public static string EnsureArchiveExtension(string archiveName, string normalizedMode)
    {
        var isCompressedMode = string.Equals(normalizedMode, Compressed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedMode, DiscardRestorableDataAndCompressed, StringComparison.OrdinalIgnoreCase);

        if (!isCompressedMode || archiveName.EndsWith(CompressedFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return archiveName;
        }

        return archiveName + CompressedFileExtension;
    }
}
