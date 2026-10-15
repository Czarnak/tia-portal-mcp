using TiaMcpServer.Cli;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

public class WorkerTimeoutStartupTests
{
    [Fact]
    public async Task StartupReportsDefaultTimeout()
    {
        var result = await StartupProcess.RunAsync(null);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("TIA MCP worker timeout: 5 minutes", result.StandardError);
    }

    [Theory]
    [InlineData("--worker-timeout-minutes", "10")]
    [InlineData("--worker-timeout-minutes=10")]
    public async Task StartupReportsRaisedTimeout(params string[] args)
    {
        var result = await StartupProcess.RunAsync(null, args);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("TIA MCP worker timeout: 10 minutes", result.StandardError);
    }

    [Fact]
    public async Task StartupUsesEnvironmentTimeout()
    {
        var result = await StartupProcess.RunAsync(new Dictionary<string, string>
        {
            [WorkerTimeoutParser.EnvVar] = "30"
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("TIA MCP worker timeout: 30 minutes", result.StandardError);
    }

    [Fact]
    public async Task StartupRejectsInvalidTimeout()
    {
        var result = await StartupProcess.RunAsync(null, "--worker-timeout-minutes", "61");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("Error: Invalid --worker-timeout-minutes value '61'. Expected a whole number of minutes from 5 to 60." + Environment.NewLine,
            result.StandardError);
        Assert.Empty(result.StandardOutput);
    }
}
