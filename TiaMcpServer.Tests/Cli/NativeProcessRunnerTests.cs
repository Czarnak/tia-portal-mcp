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

    // --- BuildProcessArgs tests ---

    [Fact]
    public void BuildProcessArgs_NativeExe_UsesResolvedPathDirectly()
    {
        var cmd = new NativeCommand("claude", new[] { "mcp", "add" }, false,
            @"C:\tools\claude.exe", ExecutableKind.Native);

        var (fileName, args) = NativeProcessRunner.BuildProcessArgs(cmd);

        Assert.Equal(@"C:\tools\claude.exe", fileName);
        Assert.Equal(new[] { "mcp", "add" }, args);
    }

    [Fact]
    public void BuildProcessArgs_CmdScript_UsesComspec()
    {
        var cmd = new NativeCommand("claude", new[] { "mcp", "add" }, false,
            @"C:\Users\user\AppData\Roaming\npm\claude.cmd", ExecutableKind.CommandScript);

        var (fileName, args) = NativeProcessRunner.BuildProcessArgs(cmd);

        var comspec = Environment.GetEnvironmentVariable("COMSPEC")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        Assert.Equal(comspec, fileName);
        Assert.Equal("/d", args[0]);
        Assert.Equal("/s", args[1]);
        Assert.Equal("/c", args[2]);
        Assert.Equal(@"C:\Users\user\AppData\Roaming\npm\claude.cmd", args[3]);
        Assert.Equal("mcp", args[4]);
        Assert.Equal("add", args[5]);
    }

    [Fact]
    public void BuildProcessArgs_BatchScript_UsesComspec()
    {
        var cmd = new NativeCommand("tool", new[] { "arg1" }, false,
            @"C:\tools\tool.bat", ExecutableKind.BatchScript);

        var (fileName, args) = NativeProcessRunner.BuildProcessArgs(cmd);

        var comspec = Environment.GetEnvironmentVariable("COMSPEC")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        Assert.Equal(comspec, fileName);
        Assert.Equal("/d", args[0]);
        Assert.Equal("/s", args[1]);
        Assert.Equal("/c", args[2]);
        Assert.Equal(@"C:\tools\tool.bat", args[3]);
        Assert.Equal("arg1", args[4]);
    }

    [Fact]
    public void BuildProcessArgs_NoResolvedPath_FallsBackToExecutable()
    {
        var cmd = new NativeCommand("cmd.exe", new[] { "/c", "echo test" }, false);

        var (fileName, args) = NativeProcessRunner.BuildProcessArgs(cmd);

        Assert.Equal("cmd.exe", fileName);
        Assert.Equal(new[] { "/c", "echo test" }, args);
    }

    [Fact]
    public void BuildProcessArgs_CmdScript_PutsMcpArgsAfterScript()
    {
        var mcpArgs = new[] { "mcp", "add", "--scope", "user", "tia-portal", "--",
            @"C:\tools\tia-mcp.exe", "--access-mode", "read-only" };
        var cmd = new NativeCommand("claude", mcpArgs, false,
            @"C:\npm\claude.cmd", ExecutableKind.CommandScript);

        var (fileName, args) = NativeProcessRunner.BuildProcessArgs(cmd);

        // args[0..2] = /d /s /c
        // args[3] = script path
        // args[4..] = MCP arguments
        Assert.Equal(@"C:\npm\claude.cmd", args[3]);
        Assert.Equal("mcp", args[4]);
        Assert.Equal("add", args[5]);
        Assert.Equal("--scope", args[6]);
        Assert.Equal("user", args[7]);
        Assert.Equal("tia-portal", args[8]);
        Assert.Equal("--", args[9]);
        Assert.Equal(@"C:\tools\tia-mcp.exe", args[10]);
        Assert.Equal("--access-mode", args[11]);
        Assert.Equal("read-only", args[12]);
    }

    [Fact]
    public void BuildProcessArgs_CmdScriptWithSpaces_PreservesPath()
    {
        var cmd = new NativeCommand("claude", new[] { "mcp", "add" }, false,
            @"C:\Users\Allan Marum\AppData\Roaming\npm\claude.cmd", ExecutableKind.CommandScript);

        var (fileName, args) = NativeProcessRunner.BuildProcessArgs(cmd);

        // The script path is passed as a separate argument, not concatenated
        Assert.Equal(@"C:\Users\Allan Marum\AppData\Roaming\npm\claude.cmd", args[3]);
    }

    [Fact]
    public void BuildProcessArgs_Interactive_UsesSameLogic()
    {
        var cmd = new NativeCommand("mimo", new[] { "mcp", "add" }, true,
            @"C:\npm\mimo.cmd", ExecutableKind.CommandScript);

        var (fileName, args) = NativeProcessRunner.BuildProcessArgs(cmd);

        var comspec = Environment.GetEnvironmentVariable("COMSPEC")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        Assert.Equal(comspec, fileName);
        Assert.Equal("/d", args[0]);
        Assert.Equal("/s", args[1]);
        Assert.Equal("/c", args[2]);
        Assert.Equal(@"C:\npm\mimo.cmd", args[3]);
    }

    // --- .cmd execution end-to-end ---

    [Fact]
    public async Task RunAsync_CmdScript_ExecutesThroughComspec()
    {
        // Create a temporary .cmd file
        var tempDir = Path.Combine(Path.GetTempPath(), "test_cmd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var cmdFile = Path.Combine(tempDir, "test.cmd");
        File.WriteAllText(cmdFile, "@echo hello_from_cmd");

        try
        {
            var runner = new NativeProcessRunner();
            var cmd = new NativeCommand("test", System.Array.Empty<string>(), false,
                cmdFile, ExecutableKind.CommandScript);

            var result = await runner.RunAsync(cmd, CancellationToken.None);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("hello_from_cmd", result.Stdout);
        }
        finally
        {
            File.Delete(cmdFile);
            Directory.Delete(tempDir);
        }
    }

    [Fact]
    public async Task RunAsync_BatchScript_ExecutesThroughComspec()
    {
        // Create a temporary .bat file
        var tempDir = Path.Combine(Path.GetTempPath(), "test_bat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var batFile = Path.Combine(tempDir, "test.bat");
        File.WriteAllText(batFile, "@echo hello_from_bat");

        try
        {
            var runner = new NativeProcessRunner();
            var cmd = new NativeCommand("test", System.Array.Empty<string>(), false,
                batFile, ExecutableKind.BatchScript);

            var result = await runner.RunAsync(cmd, CancellationToken.None);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("hello_from_bat", result.Stdout);
        }
        finally
        {
            File.Delete(batFile);
            Directory.Delete(tempDir);
        }
    }

    // --- ProcessStartInfo validation ---

    [Fact]
    public void BuildProcessArgs_Native_VerifyProcessStartInfo()
    {
        var cmd = new NativeCommand("claude", new[] { "mcp", "add" }, false,
            @"C:\tools\claude.exe", ExecutableKind.Native);

        var (fileName, args) = NativeProcessRunner.BuildProcessArgs(cmd);

        // Verify the process would be started correctly
        Assert.Equal(@"C:\tools\claude.exe", fileName);
        Assert.DoesNotContain("/d", args);
        Assert.DoesNotContain("/s", args);
        Assert.DoesNotContain("/c", args);
    }

    [Fact]
    public void BuildProcessArgs_Cmd_VerifyProcessStartInfo()
    {
        var cmd = new NativeCommand("claude", new[] { "mcp", "add" }, false,
            @"C:\npm\claude.cmd", ExecutableKind.CommandScript);

        var (fileName, args) = NativeProcessRunner.BuildProcessArgs(cmd);

        var comspec = Environment.GetEnvironmentVariable("COMSPEC")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        Assert.Equal(comspec, fileName);
        Assert.Equal("/d", args[0]);
        Assert.Equal("/s", args[1]);
        Assert.Equal("/c", args[2]);
        Assert.Equal(@"C:\npm\claude.cmd", args[3]);
        // MCP args come after
        Assert.Equal("mcp", args[4]);
        Assert.Equal("add", args[5]);
    }
}
