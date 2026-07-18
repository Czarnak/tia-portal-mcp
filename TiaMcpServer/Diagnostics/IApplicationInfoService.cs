namespace TiaMcpServer.Diagnostics;

public interface IApplicationInfoService
{
    bool IsWindows { get; }

    string OsName { get; }

    string OsVersion { get; }

    string ProcessArchitecture { get; }

    string HostVersion { get; }

    string BaseDirectory { get; }

    string RuntimeDescription { get; }
}
