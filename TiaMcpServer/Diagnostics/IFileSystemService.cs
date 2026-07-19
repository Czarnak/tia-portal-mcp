namespace TiaMcpServer.Diagnostics;

public interface IFileSystemService
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    string? GetFileVersion(string path);
}
