using System.Diagnostics;

namespace TiaMcpServer.Diagnostics;

public sealed class FileSystemService : IFileSystemService
{
    public static readonly FileSystemService Instance = new();

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string? GetFileVersion(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var version = !string.IsNullOrWhiteSpace(info.ProductVersion)
                ? info.ProductVersion
                : !string.IsNullOrWhiteSpace(info.FileVersion)
                    ? info.FileVersion
                    : null;
            return version;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            return null;
        }
    }
}
