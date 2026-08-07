using System.Diagnostics;
using TiaMcpServer.Cli.Install;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

public sealed class FinalInstallCoverageTests
{
    private sealed class FakeProcessRunner : INativeProcessRunner
    {
        public Task<NativeCommandResult> RunAsync(
            NativeCommand command,
            CancellationToken cancellationToken)
            => Task.FromResult(new NativeCommandResult(0, string.Empty, string.Empty));
    }

    private static string? ResolveServerExecutable(string? path)
        => path ?? @"C:\tools\tia-mcp.exe";

    private static ExecutableResolutionResult ResolveCommandScript(string executable)
        => new(
            true,
            executable,
            $@"C:\npm\{executable}.cmd",
            ExecutableKind.CommandScript,
            null);

    [Fact]
    public void RunWhereAll_ProcessStartFailure_ReturnsEmptyResult()
    {
        Process? ThrowingStarter(ProcessStartInfo _)
            => throw new InvalidOperationException("process start failed");

        var results = ExecutableResolver.RunWhereAll(
            "client",
            ThrowingStarter);

        Assert.Empty(results);
    }

    [Fact]
    public async Task RunAsync_DryRunJsonWithCommandShim_FormatsCommandScriptKind()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            new[] { "codex", "--dry-run", "--json" },
            new FakeProcessRunner(),
            output,
            error,
            ResolveServerExecutable,
            ResolveCommandScript);

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "\"clientExecutableKind\":\"command_script\"",
            output.ToString());
    }
}
