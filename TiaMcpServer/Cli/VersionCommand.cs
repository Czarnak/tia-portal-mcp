using TiaMcpServer.Diagnostics;

namespace TiaMcpServer.Cli;

public static class VersionCommand
{
    public static int Run(TextWriter output)
        => Run(output, ApplicationInfoService.Instance);

    public static int Run(TextWriter output, IApplicationInfoService appInfo)
    {
        output.WriteLine(appInfo.HostVersion);
        return 0;
    }
}
