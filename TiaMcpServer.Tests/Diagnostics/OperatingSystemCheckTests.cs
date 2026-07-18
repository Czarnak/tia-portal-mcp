using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class OperatingSystemCheckTests
{
    [Fact]
    public void Windows_ReturnsPassed()
    {
        var appInfo = new FakeApplicationInfoService { IsWindows = true, OsName = "Windows", OsVersion = "10.0", ProcessArchitecture = "X64" };
        var check = new OperatingSystemCheck(appInfo);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("Windows", result.Message);
    }

    [Fact]
    public void NonWindows_ReturnsFailed()
    {
        var appInfo = new FakeApplicationInfoService { IsWindows = false, OsName = "Linux", OsVersion = "6.0" };
        var check = new OperatingSystemCheck(appInfo);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("not supported", result.Message);
        Assert.NotNull(result.Remediation);
    }

    [Fact]
    public void IncludesEvidence()
    {
        var appInfo = new FakeApplicationInfoService { IsWindows = true, OsName = "Windows", OsVersion = "10.0", ProcessArchitecture = "Arm64" };
        var check = new OperatingSystemCheck(appInfo);

        var result = check.Run();

        Assert.NotNull(result.Evidence);
        Assert.Equal("true", result.Evidence!["isWindows"]);
        Assert.Equal("Windows", result.Evidence!["osName"]);
        Assert.Equal("Arm64", result.Evidence!["processArchitecture"]);
    }
}
