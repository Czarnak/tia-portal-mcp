using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class TiaPortalProcessCheckTests
{
    [Fact]
    public void NotWindows_ReturnsWarning()
    {
        var processes = new FakeProcessEnumerationService();
        var appInfo = new FakeApplicationInfoService { IsWindows = false };
        var check = new TiaPortalProcessCheck(
            processes,
            appInfo,
            McpAccessMode.ReadOnly,
            hasConfiguredProjectBinding: false);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("Not running on Windows", result.Message);
    }

    [Fact]
    public void TiaProcessesFound_ReturnsPassed()
    {
        var processes = new FakeProcessEnumerationService
        {
            Processes = new()
            {
                new("Siemens.Automation.Portal.exe", 1234),
                new("notepad.exe", 5678)
            }
        };
        var appInfo = new FakeApplicationInfoService { IsWindows = true };
        var check = new TiaPortalProcessCheck(
            processes,
            appInfo,
            McpAccessMode.ReadOnly,
            hasConfiguredProjectBinding: false);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("1 process", result.Message);
    }

    [Fact]
    public void MultipleTiaProcesses_UnboundReadOnly_ReturnsWarningAndCountsAll()
    {
        var processes = new FakeProcessEnumerationService
        {
            Processes = new()
            {
                new("Siemens.Automation.Portal.exe", 100),
                new("TIA.Portal.Startcenter.exe", 200),
                new("Siemens.Simulation.Portal.exe", 300)
            }
        };
        var appInfo = new FakeApplicationInfoService { IsWindows = true };
        var check = new TiaPortalProcessCheck(
            processes,
            appInfo,
            McpAccessMode.ReadOnly,
            hasConfiguredProjectBinding: false);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("3 process", result.Message);
        Assert.Contains("bind_project", result.Remediation);
    }

    [Theory]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public void Doctor_MultipleProcessesUnbound_Warning(McpAccessMode mode)
    {
        var processes = new FakeProcessEnumerationService
        {
            Processes = new()
            {
                new("Siemens.Automation.Portal.exe", 100),
                new("TIA.Portal.Startcenter.exe", 200)
            }
        };
        var appInfo = new FakeApplicationInfoService { IsWindows = true };
        var check = new TiaPortalProcessCheck(
            processes,
            appInfo,
            mode,
            hasConfiguredProjectBinding: false);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains(McpAccessModeNames.ToName(mode), result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(McpAccessModeNames.ToName(mode), result.Evidence!["accessMode"]);
        Assert.Contains("no explicit project binding", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bind_project", result.Remediation);
        Assert.Contains("--project", result.Remediation);
        Assert.True(result.Remediation!.IndexOf("bind_project", StringComparison.Ordinal)
            < result.Remediation.IndexOf("--project", StringComparison.Ordinal));
    }

    [Fact]
    public void MultipleTiaProcesses_BoundReadWrite_ReturnsWarningBecauseLiveMatchIsUnknown()
    {
        var processes = new FakeProcessEnumerationService
        {
            Processes = new()
            {
                new("Siemens.Automation.Portal.exe", 100),
                new("TIA.Portal.Startcenter.exe", 200)
            }
        };
        var appInfo = new FakeApplicationInfoService { IsWindows = true };
        var check = new TiaPortalProcessCheck(
            processes,
            appInfo,
            McpAccessMode.ReadWrite,
            hasConfiguredProjectBinding: true);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("cannot determine", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoTiaProcesses_ReturnsWarning()
    {
        var processes = new FakeProcessEnumerationService
        {
            Processes = new() { new("notepad.exe", 1234) }
        };
        var appInfo = new FakeApplicationInfoService { IsWindows = true };
        var check = new TiaPortalProcessCheck(
            processes,
            appInfo,
            McpAccessMode.ReadOnly,
            hasConfiguredProjectBinding: false);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("No running TIA Portal", result.Message);
    }

    [Fact]
    public void CaseInsensitiveMatch()
    {
        var processes = new FakeProcessEnumerationService
        {
            Processes = new() { new("siemens.automation.portal.exe", 1234) }
        };
        var appInfo = new FakeApplicationInfoService { IsWindows = true };
        var check = new TiaPortalProcessCheck(
            processes,
            appInfo,
            McpAccessMode.ReadOnly,
            hasConfiguredProjectBinding: false);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
    }
}
