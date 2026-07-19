using Microsoft.Win32;
using System.Runtime.Versioning;
using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

[SupportedOSPlatform("windows")]
public class DotNetFrameworkCheckTests
{
    private const string NetFrameworkKey = @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full";

    [Fact]
    public void NotWindows_ReturnsFailed()
    {
        var registry = new FakeRegistryService();
        var appInfo = new FakeApplicationInfoService { IsWindows = false };
        var check = new DotNetFrameworkCheck(registry, appInfo);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("Not running on Windows", result.Message);
    }

    [Fact]
    public void NoRegistryValue_ReturnsFailed()
    {
        var registry = new FakeRegistryService();
        var appInfo = new FakeApplicationInfoService { IsWindows = true };
        var check = new DotNetFrameworkCheck(registry, appInfo);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("was not detected", result.Message);
    }

    [Fact]
    public void OldRelease_ReturnsFailed()
    {
        var registry = new FakeRegistryService();
        registry.SetIntValue(RegistryHive.LocalMachine, RegistryView.Registry64, NetFrameworkKey, "Release", 461808); // 4.7.2
        var appInfo = new FakeApplicationInfoService { IsWindows = true };
        var check = new DotNetFrameworkCheck(registry, appInfo);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("4.7.2", result.Message);
        Assert.Contains("4.8", result.Message);
    }

    [Fact]
    public void Release48_ReturnsPassed()
    {
        var registry = new FakeRegistryService();
        registry.SetIntValue(RegistryHive.LocalMachine, RegistryView.Registry64, NetFrameworkKey, "Release", 528040);
        var appInfo = new FakeApplicationInfoService { IsWindows = true };
        var check = new DotNetFrameworkCheck(registry, appInfo);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("4.8", result.Message);
    }

    [Fact]
    public void Release481_ReturnsPassed()
    {
        var registry = new FakeRegistryService();
        registry.SetIntValue(RegistryHive.LocalMachine, RegistryView.Registry64, NetFrameworkKey, "Release", 533320);
        var appInfo = new FakeApplicationInfoService { IsWindows = true };
        var check = new DotNetFrameworkCheck(registry, appInfo);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("4.8.1", result.Message);
    }

    [Fact]
    public void FallsBackToRegistry32()
    {
        var registry = new FakeRegistryService();
        // Registry64 returns null, Registry32 returns 528040
        registry.SetIntValue(RegistryHive.LocalMachine, RegistryView.Registry32, NetFrameworkKey, "Release", 528040);
        var appInfo = new FakeApplicationInfoService { IsWindows = true };
        var check = new DotNetFrameworkCheck(registry, appInfo);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Equal("Registry32", result.Evidence!["registryView"]);
    }
}
