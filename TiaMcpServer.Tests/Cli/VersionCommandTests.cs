using TiaMcpServer.Cli;
using TiaMcpServer.Tests.Diagnostics;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

public class VersionCommandTests
{
    [Fact]
    public void Run_WritesHostVersionToOutputAndReturnsZero()
    {
        var appInfo = new FakeApplicationInfoService { HostVersion = "2.3.2-local.42.g1d987da" };
        var output = new StringWriter();

        var exitCode = VersionCommand.Run(output, appInfo);

        Assert.Equal(0, exitCode);
        Assert.Equal("2.3.2-local.42.g1d987da" + Environment.NewLine, output.ToString());
    }
}
