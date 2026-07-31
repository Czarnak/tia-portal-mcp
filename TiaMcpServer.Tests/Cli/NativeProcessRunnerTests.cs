using TiaMcpServer.Cli.Install;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

public class NativeProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_ExitCodeZero_ReturnsZero()
    {
        var runner = new NativeProcessRunner();
        var cmd = new NativeCommand("cmd.exe", new[] { "/c", "exit 0" }, false);

        var result = await runner.RunAsync(cmd, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_ExitCodeNonzero_ReturnsNonzero()
    {
        var runner = new NativeProcessRunner();
        var cmd = new NativeCommand("cmd.exe", new[] { "/c", "exit 42" }, false);

        var result = await runner.RunAsync(cmd, CancellationToken.None);

        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_CapturesStdout()
    {
        var runner = new NativeProcessRunner();
        var cmd = new NativeCommand("cmd.exe", new[] { "/c", "echo hello" }, false);

        var result = await runner.RunAsync(cmd, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.Stdout);
    }

    [Fact]
    public async Task RunAsync_CapturesStderr()
    {
        var runner = new NativeProcessRunner();
        var cmd = new NativeCommand("cmd.exe", new[] { "/c", "echo error>&2" }, false);

        var result = await runner.RunAsync(cmd, CancellationToken.None);

        Assert.Contains("error", result.Stderr);
    }

    [Fact]
    public async Task RunAsync_InvalidExecutable_ReturnsNegativeExitCode()
    {
        var runner = new NativeProcessRunner();
        var cmd = new NativeCommand("nonexistent_command_xyz.exe", System.Array.Empty<string>(), false);

        var result = await runner.RunAsync(cmd, CancellationToken.None);

        Assert.Equal(-1, result.ExitCode);
        Assert.NotEmpty(result.Stderr);
    }

    [Fact]
    public async Task RunAsync_Cancellation_ReturnsCancelledResult()
    {
        var runner = new NativeProcessRunner();
        var cmd = new NativeCommand("cmd.exe", new[] { "/c", "ping 127.0.0.1 -n 30" }, false);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var result = await runner.RunAsync(cmd, cts.Token);

        // Cancellation may result in exit code -1 (OperationCanceledException) or
        // a nonzero exit code if the process was killed after the token fired.
        Assert.Contains("cancel", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunAsync_Interactive_HasInteractiveFlag()
    {
        var runner = new NativeProcessRunner();
        var cmd = new NativeCommand("cmd.exe", new[] { "/c", "echo test" }, true);

        Assert.True(cmd.Interactive);
    }

    [Fact]
    public async Task RunAsync_NonInteractive_CapturesOutput()
    {
        var runner = new NativeProcessRunner();
        var cmd = new NativeCommand("cmd.exe", new[] { "/c", "echo captured" }, false);

        var result = await runner.RunAsync(cmd, CancellationToken.None);

        Assert.Contains("captured", result.Stdout);
        Assert.False(cmd.Interactive);
    }
}
