using TiaMcpServer.Cli.Install;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

public class InstallCommandTests
{
    private sealed class FakeProcessRunner : INativeProcessRunner
    {
        public List<NativeCommand> ExecutedCommands { get; } = new();
        public Queue<NativeCommandResult> Results { get; } = new();

        public Task<NativeCommandResult> RunAsync(NativeCommand command, CancellationToken cancellationToken)
        {
            ExecutedCommands.Add(command);
            if (Results.Count > 0)
            {
                return Task.FromResult(Results.Dequeue());
            }

            return Task.FromResult(new NativeCommandResult(0, string.Empty, string.Empty));
        }
    }

    private static string? FakeResolveServerExe(string? path) => path ?? @"C:\tools\tia-mcp.exe";

    private static ExecutableResolutionResult FakeResolveClientExe(string exe)
        => new(true, exe, $@"C:\tools\{exe}.exe", ExecutableKind.Native, null);

    private static ExecutableResolutionResult FakeResolveClientExeAsCmd(string exe)
        => new(true, exe, $@"C:\Users\user\AppData\Roaming\npm\{exe}.cmd", ExecutableKind.CommandScript, null);

    private static ExecutableResolutionResult NotFoundClientExe(string exe)
        => new(false, exe, null, ExecutableKind.Native, $"The executable '{exe}' was not found.");

    [Fact]
    public async Task RunAsync_Help_ReturnsZeroAndPrintsUsage()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            new[] { "--help" }, runner, output, error, FakeResolveServerExe, FakeResolveClientExe);

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage: tia-mcp install", output.ToString());
    }

    [Fact]
    public async Task RunAsync_HelpJson_ReturnsZeroAndJsonUsage()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            new[] { "--help", "--json" }, runner, output, error, FakeResolveServerExe, FakeResolveClientExe);

        Assert.Equal(0, exitCode);
        Assert.Contains("\"usage\"", output.ToString());
    }

    [Fact]
    public async Task RunAsync_MissingClient_ReturnsExitCode2()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            System.Array.Empty<string>(), runner, output, error, FakeResolveServerExe, FakeResolveClientExe);

        Assert.Equal(2, exitCode);
        Assert.Contains("No MCP client specified", error.ToString());
    }

    [Fact]
    public async Task RunAsync_UnsupportedClient_ReturnsExitCode3()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            new[] { "unknown-client" }, runner, output, error, FakeResolveServerExe, FakeResolveClientExe);

        Assert.Equal(3, exitCode);
    }

    [Fact]
    public async Task RunAsync_ServerExeNotFound_ReturnsExitCode5()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();
        string? NullResolver(string? _) => null;

        var exitCode = await InstallCommand.RunAsync(
            new[] { "codex" }, runner, output, error, NullResolver, FakeResolveClientExe);

        Assert.Equal(5, exitCode);
        Assert.Contains("tia-mcp executable not found", error.ToString());
    }

    [Fact]
    public async Task RunAsync_ClientNotFound_ReturnsExitCode4()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            new[] { "codex" }, runner, output, error, FakeResolveServerExe, NotFoundClientExe);

        Assert.Equal(4, exitCode);
    }

    [Fact]
    public async Task RunAsync_MiMoCode_UsesInteractiveMode()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        // Install succeeds (interactive)
        runner.Results.Enqueue(new NativeCommandResult(0, string.Empty, string.Empty));
        // Verification succeeds
        runner.Results.Enqueue(new NativeCommandResult(0, "3 server(s)", string.Empty));

        var exitCode = await InstallCommand.RunAsync(
            new[] { "mimo" }, runner, output, error, FakeResolveServerExe, FakeResolveClientExe);

        Assert.Equal(0, exitCode);
        Assert.Equal("mimo", runner.ExecutedCommands[0].Executable);
        Assert.True(runner.ExecutedCommands[0].Interactive);
    }

    [Fact]
    public async Task RunAsync_MiMoCodeWithJson_ReturnsExitCode8()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            new[] { "mimo", "--json" }, runner, output, error, FakeResolveServerExe, FakeResolveClientExe);

        Assert.Equal(8, exitCode);
        Assert.Contains("does not support --json", output.ToString());
    }

    [Fact]
    public async Task RunAsync_DryRun_ReturnsZeroAndPrintsCommand()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            new[] { "codex", "--dry-run" }, runner, output, error, FakeResolveServerExe, FakeResolveClientExe);

        Assert.Equal(0, exitCode);
        Assert.Contains("Dry run", output.ToString());
        Assert.Contains("codex", output.ToString());
    }

    [Fact]
    public async Task RunAsync_DryRunJson_ReturnsZeroAndJsonDryRun()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            new[] { "codex", "--dry-run", "--json" }, runner, output, error, FakeResolveServerExe, FakeResolveClientExe);

        Assert.Equal(0, exitCode);
        Assert.Contains("\"dryRun\":true", output.ToString());
    }

    [Fact]
    public async Task RunAsync_InstallSuccess_ReturnsZero()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        // Install succeeds
        runner.Results.Enqueue(new NativeCommandResult(0, string.Empty, string.Empty));
        // Verification succeeds
        runner.Results.Enqueue(new NativeCommandResult(0, "{}", string.Empty));

        var exitCode = await InstallCommand.RunAsync(
            new[] { "codex" }, runner, output, error, FakeResolveServerExe, FakeResolveClientExe);

        Assert.Equal(0, exitCode);
        Assert.Contains("Successfully registered", output.ToString());
        Assert.Contains("Verification: passed", output.ToString());
    }

    [Fact]
    public async Task RunAsync_InstallSuccess_ExecutesCorrectCommand()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        // Install succeeds
        runner.Results.Enqueue(new NativeCommandResult(0, string.Empty, string.Empty));
        // Verification succeeds
        runner.Results.Enqueue(new NativeCommandResult(0, "{}", string.Empty));

        await InstallCommand.RunAsync(
            new[] { "codex", "--name", "my-tia", "--access-mode", "read-write" },
            runner, output, error, FakeResolveServerExe, FakeResolveClientExe);

        // First command is install, second is verification
        Assert.Equal(2, runner.ExecutedCommands.Count);
        Assert.Equal("codex", runner.ExecutedCommands[0].Executable);
        Assert.Contains("mcp", runner.ExecutedCommands[0].Arguments);
        Assert.Contains("add", runner.ExecutedCommands[0].Arguments);
        Assert.Contains("my-tia", runner.ExecutedCommands[0].Arguments);
    }

    [Fact]
    public async Task RunAsync_InstallFailed_ReturnsExitCode6()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        // Install fails
        runner.Results.Enqueue(new NativeCommandResult(1, string.Empty, "Installation failed"));

        var exitCode = await InstallCommand.RunAsync(
            new[] { "codex" }, runner, output, error, FakeResolveServerExe, FakeResolveClientExe);

        Assert.Equal(6, exitCode);
        Assert.Contains("Install command failed", error.ToString());
    }

    [Fact]
    public async Task RunAsync_VerificationFailed_ReturnsExitCode7()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        // Install succeeds
        runner.Results.Enqueue(new NativeCommandResult(0, string.Empty, string.Empty));
        // Verification fails
        runner.Results.Enqueue(new NativeCommandResult(1, string.Empty, "Verification error"));

        var exitCode = await InstallCommand.RunAsync(
            new[] { "codex" }, runner, output, error, FakeResolveServerExe, FakeResolveClientExe);

        Assert.Equal(7, exitCode);
    }

    [Fact]
    public async Task RunAsync_JsonSuccess_OutputsJson()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        // Install succeeds
        runner.Results.Enqueue(new NativeCommandResult(0, string.Empty, string.Empty));
        // Verification succeeds
        runner.Results.Enqueue(new NativeCommandResult(0, "{}", string.Empty));

        var exitCode = await InstallCommand.RunAsync(
            new[] { "codex", "--json" }, runner, output, error, FakeResolveServerExe, FakeResolveClientExe);

        Assert.Equal(0, exitCode);
        var jsonOutput = output.ToString();
        Assert.Contains("\"success\":true", jsonOutput);
        Assert.Contains("\"client\":\"Codex\"", jsonOutput);
        Assert.Contains("\"serverName\":\"tia-portal\"", jsonOutput);
        Assert.Contains("\"accessMode\":\"read-only\"", jsonOutput);
        Assert.Contains("\"resolvedClientPath\"", jsonOutput);
        Assert.Contains("\"clientExecutableKind\"", jsonOutput);
    }

    [Fact]
    public async Task RunAsync_JsonFailure_OutputsJsonError()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();
        string? NullResolver(string? _) => null;

        var exitCode = await InstallCommand.RunAsync(
            new[] { "codex", "--json" }, runner, output, error, NullResolver, FakeResolveClientExe);

        Assert.Equal(5, exitCode);
        Assert.Contains("\"success\":false", output.ToString());
        Assert.Contains("\"tia_mcp_executable_not_found\"", output.ToString());
    }

    [Fact]
    public async Task RunAsync_WithTiaProject_IncludesProjectInCommand()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        // Install succeeds
        runner.Results.Enqueue(new NativeCommandResult(0, string.Empty, string.Empty));
        // Verification succeeds
        runner.Results.Enqueue(new NativeCommandResult(0, "{}", string.Empty));

        await InstallCommand.RunAsync(
            new[] { "codex", "--tia-project", @"C:\Projects\Line.ap21" },
            runner, output, error, FakeResolveServerExe, FakeResolveClientExe);

        var installCmd = runner.ExecutedCommands[0];
        Assert.Contains("--project", installCmd.Arguments);
        Assert.Contains(@"C:\Projects\Line.ap21", installCmd.Arguments);
    }

    [Fact]
    public async Task RunAsync_ClaudeCode_UsesCorrectExecutable()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        // Install succeeds
        runner.Results.Enqueue(new NativeCommandResult(0, string.Empty, string.Empty));
        // Verification succeeds
        runner.Results.Enqueue(new NativeCommandResult(0, "{}", string.Empty));

        await InstallCommand.RunAsync(
            new[] { "claude" }, runner, output, error, FakeResolveServerExe, FakeResolveClientExe);

        Assert.Equal("claude", runner.ExecutedCommands[0].Executable);
        Assert.Contains("--scope", runner.ExecutedCommands[0].Arguments);
        Assert.Contains("user", runner.ExecutedCommands[0].Arguments);
    }

    [Fact]
    public async Task RunAsync_OpenCode_UsesCorrectExecutable()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        // Install succeeds
        runner.Results.Enqueue(new NativeCommandResult(0, string.Empty, string.Empty));
        // Verification succeeds
        runner.Results.Enqueue(new NativeCommandResult(0, "{}", string.Empty));

        await InstallCommand.RunAsync(
            new[] { "opencode" }, runner, output, error, FakeResolveServerExe, FakeResolveClientExe);

        Assert.Equal("opencode", runner.ExecutedCommands[0].Executable);
    }

    [Fact]
    public async Task RunAsync_CmdShim_PatchesResolvedPathAndKind()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        // Install succeeds
        runner.Results.Enqueue(new NativeCommandResult(0, string.Empty, string.Empty));
        // Verification succeeds
        runner.Results.Enqueue(new NativeCommandResult(0, "{}", string.Empty));

        await InstallCommand.RunAsync(
            new[] { "claude" }, runner, output, error, FakeResolveServerExe, FakeResolveClientExeAsCmd);

        var installCmd = runner.ExecutedCommands[0];
        Assert.Equal(ExecutableKind.CommandScript, installCmd.Kind);
        Assert.Equal(@"C:\Users\user\AppData\Roaming\npm\claude.cmd", installCmd.ResolvedPath);
    }

    [Fact]
    public async Task RunAsync_ClientNotFound_JsonOutput_HasErrorCode()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            new[] { "claude", "--json" }, runner, output, error, FakeResolveServerExe, NotFoundClientExe);

        Assert.Equal(4, exitCode);
        var jsonOutput = output.ToString();
        Assert.Contains("\"client_not_found\"", jsonOutput);
        Assert.Contains("\"success\":false", jsonOutput);
    }

    [Fact]
    public async Task RunAsync_DryRun_CmdShim_ShowsExecutionMethod()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            new[] { "claude", "--dry-run" }, runner, output, error, FakeResolveServerExe, FakeResolveClientExeAsCmd);

        Assert.Equal(0, exitCode);
        var text = output.ToString();
        Assert.Contains("Execution method:", text);
        Assert.Contains("/d /s /c", text);
        Assert.Contains("claude.cmd", text);
    }

    [Fact]
    public async Task RunAsync_InstallFailed_Json_HasErrorCode()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        // Install fails
        runner.Results.Enqueue(new NativeCommandResult(1, string.Empty, "Installation failed"));

        var exitCode = await InstallCommand.RunAsync(
            new[] { "codex", "--json" }, runner, output, error, FakeResolveServerExe, FakeResolveClientExe);

        Assert.Equal(6, exitCode);
        var jsonOutput = output.ToString();
        Assert.Contains("\"client_command_failed\"", jsonOutput);
        Assert.Contains("\"success\":false", jsonOutput);
    }
}
