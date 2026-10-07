namespace TiaMcpServer.Safety.Pipeline;

/// <summary>Project-path normalization shared by every write catalog.</summary>
public static class WriteProjectPaths
{
    /// <summary>Absolute form of <paramref name="projectPath"/>, or <c>(active)</c> when blank.</summary>
    public static string Normalize(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return "(active)";
        }

        try
        {
            return Path.GetFullPath(projectPath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return projectPath.Trim();
        }
    }
}
