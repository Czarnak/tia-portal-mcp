using System.Diagnostics;
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
        var result = await RunStartupAsync(flag);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("Error: " + MigrationMessage + Environment.NewLine, result.StandardError);
        Assert.Empty(result.StandardOutput);
    }

    [Fact]
    public async Task NoFlag_StartupUnaffected()
    {
        var result = await RunStartupAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("TIA MCP access mode: READ-WRITE", result.StandardError);
        Assert.DoesNotContain(MigrationMessage, result.StandardError);
    }

    [Fact]
    public async Task StartupBanner_OmitsConfirmationSetting()
    {
        var result = await RunStartupAsync("--access-mode", "full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("TIA MCP access mode: FULL", result.StandardError);
        Assert.DoesNotContain("confirm with user", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<StartupResult> RunStartupAsync(params string[] args)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "run", "--no-build", "--no-restore", "--project", "TiaMcpServer", "--configuration", configuration, "--" }.Concat(args))
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.Environment.Remove("TIA_MCP_ACCESS_MODE");
        startInfo.Environment.Remove("TIA_PROJECT_PATH");
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the MCP host.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new StartupResult(process.ExitCode, await standardOutput.WaitAsync(timeout.Token), await standardError.WaitAsync(timeout.Token));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private sealed record StartupResult(int ExitCode, string StandardOutput, string StandardError);
}
