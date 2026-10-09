using TiaMcpServer.Cli;
using TiaMcpServer.Cli.Install;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Diagnostics;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

[Collection("Process environment")]
public class Phase1bCliModeTests
{
    [Fact]
    public void FullMode_ResolvesCliAndEnvironment_WithoutChangingDefaults()
    {
        var previous = Environment.GetEnvironmentVariable("TIA_MCP_ACCESS_MODE");
        try
        {
            Environment.SetEnvironmentVariable("TIA_MCP_ACCESS_MODE", "full");
            Assert.Equal(McpAccessMode.Full, AccessModeParser.Parse(Array.Empty<string>()).Mode);
            Assert.Equal(McpAccessMode.ReadOnly, AccessModeParser.Parse(new[] { "--read-only" }).Mode);
            Assert.Equal(McpAccessMode.Full, AccessModeParser.Parse(new[] { "--access-mode", "full" }).Mode);
            Assert.Equal(McpAccessMode.Full, AccessModeParser.Parse(new[] { "--access-mode=FULL", "--access-mode", "full" }).Mode);
            Assert.False(AccessModeParser.Parse(new[] { "--read-write", "--access-mode=full" }).IsValid);
            Assert.False(AccessModeParser.Parse(new[] { "--read-only", "--access-mode=full" }).IsValid);
            Environment.SetEnvironmentVariable("TIA_MCP_ACCESS_MODE", null);
            Assert.Equal(McpAccessMode.ReadWrite, AccessModeParser.Parse(Array.Empty<string>()).Mode);
        }
        finally { Environment.SetEnvironmentVariable("TIA_MCP_ACCESS_MODE", previous); }
    }

    [Fact]
    public void Doctor_ParsesAndReportsFullMode()
    {
        var options = DoctorCliParser.Parse(new[] { "--access-mode", "full" });
        Assert.True(options.Valid, options.ParseError);
        Assert.Equal(McpAccessMode.Full, options.AccessMode);
        var report = new DoctorReport(DiagnosticStatus.Passed, DateTimeOffset.UtcNow, "1.0.0", new DoctorSummary(0, 0, 0), Array.Empty<DiagnosticCheckResult>());
        Assert.Contains("\"accessMode\": \"full\"", DoctorJsonRenderer.Render(report, false, McpAccessMode.Full));
        using var writer = new StringWriter();
        DoctorTextRenderer.Render(report, false, writer, McpAccessMode.Full);
        Assert.Contains("Access mode: FULL", writer.ToString());
    }

    [Theory]
    [InlineData("claude-code")]
    [InlineData("codex")]
    [InlineData("opencode")]
    [InlineData("mimocode")]
    public void Install_FullIsAccepted_AndReadOnlyRemainsTheDefault(string client)
    {
        var options = InstallCliParser.Parse(new[] { client, "--access-mode", "FULL" });
        Assert.True(options.Valid, options.ParseError);
        Assert.Equal("full", options.AccessMode);
        Assert.Equal("read-only", InstallCliParser.Parse(new[] { client }).AccessMode);
    }

    [Theory]
    [InlineData("--confirm-with-user")]
    [InlineData("--confirm-with-user=false")]
    [InlineData("--confirm-with-user=TRUE")]
    public void RemovedConfirmationArguments_AreNotSilentlyFiltered(string flag)
    {
        var args = new[] { flag, "--project", "Line.ap21", "--Logging:LogLevel:Default=Debug" };
        Assert.Equal(args, HostArgumentFilter.RemoveAccessModeArguments(args));
    }
}
