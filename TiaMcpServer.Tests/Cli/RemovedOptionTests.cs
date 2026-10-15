using Xunit;

namespace TiaMcpServer.Tests.Cli;

public class RemovedOptionTests
{
    private const string MigrationMessage = "--confirm-with-user was removed. Confirmation follows the access mode: read-write asks for every lifecycle call; use --access-mode full to run lifecycle tools without prompts.";

    [Theory]
    [InlineData("--confirm-with-user")]
    [InlineData("--confirm-with-user=true")]
    [InlineData("--confirm-with-user=false")]
    [InlineData("--CONFIRM-WITH-USER")]
    [InlineData("--CONFIRM-WITH-USER=FALSE")]
    [InlineData("--confirm-with-user=")]
    [InlineData("--confirm-with-user=unknown")]
    public async Task ConfirmWithUser_AnySpelling_FailsStartupWithMigrationMessage(string flag)
    {
        var result = await StartupProcess.RunAsync(null, flag);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("Error: " + MigrationMessage + Environment.NewLine, result.StandardError);
        Assert.Empty(result.StandardOutput);
    }

    [Fact]
    public async Task NoFlag_StartupUnaffected()
    {
        var result = await StartupProcess.RunAsync(null);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("TIA MCP access mode: READ-WRITE", result.StandardError);
        Assert.DoesNotContain(MigrationMessage, result.StandardError);
    }

    [Fact]
    public async Task StartupBanner_OmitsConfirmationSetting()
    {
        var result = await StartupProcess.RunAsync(null, "--access-mode", "full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("TIA MCP access mode: FULL", result.StandardError);
        Assert.DoesNotContain("confirm with user", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }
}
