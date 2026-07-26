namespace TiaMcpServer.Contracts;

/// <summary>
/// Pure path-comparison guard for archiving into the open project's own folder or anywhere inside
/// it. Confirmed against a live TIA Portal V21 instance: archiving with the exact containing folder
/// as the target fails outright, with the opaque error "A project directory that already exists
/// cannot be saved." Archiving into a subdirectory of that folder was reported to sometimes succeed
/// but have TIA Portal silently auto-delete the subdirectory - permitted in some cases is not a
/// reason to allow it, since the failure mode is silent data loss rather than a clean rejection.
/// Both are blocked categorically. Kept dependency-free (no Siemens types) so it is callable from
/// both the host and worker processes and is directly unit-testable.
/// </summary>
public static class ArchiveDirectoryGuard
{
    /// <summary>
    /// True when <paramref name="archiveDirectory"/> resolves to the directory that contains
    /// <paramref name="projectFilePath"/> (the open project's .apXX file) or any directory nested
    /// inside it, at any depth. Both inputs are full-path-normalized and compared case-insensitively,
    /// ignoring a trailing directory separator. A sibling directory that merely shares a name prefix
    /// (e.g. "SimpleProjectBackup" next to "SimpleProject") is not flagged - the nesting check is
    /// separator-bounded.
    /// </summary>
    public static bool IsWithinProjectFolder(string archiveDirectory, string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(archiveDirectory) || string.IsNullOrWhiteSpace(projectFilePath))
        {
            return false;
        }

        var projectDirectory = Path.GetDirectoryName(projectFilePath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return false;
        }

        var normalizedArchiveDirectory = Normalize(archiveDirectory);
        var normalizedProjectDirectory = Normalize(projectDirectory);

        if (string.Equals(normalizedArchiveDirectory, normalizedProjectDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var projectDirectoryWithSeparator = normalizedProjectDirectory + Path.DirectorySeparatorChar;
        return normalizedArchiveDirectory.StartsWith(projectDirectoryWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
