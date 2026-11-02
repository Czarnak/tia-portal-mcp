namespace TiaMcpServer.Tests.TestSupport;

internal static class RepositoryFiles
{
    public static string Root { get; } = FindRoot(AppContext.BaseDirectory);

    public static string FindRoot(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "TiaMcpServer.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException($"Could not locate the repository root from '{startDirectory}'.");
    }

    public static string Path(params string[] parts)
        => System.IO.Path.Combine(Root, System.IO.Path.Combine(parts));

    public static string ReadSource(params string[] parts)
        => File.ReadAllText(Path(parts)).Replace("\r\n", "\n");

    public static int CountOccurrences(string source, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        var count = 0;
        var startIndex = 0;
        while ((startIndex = source.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }
}
