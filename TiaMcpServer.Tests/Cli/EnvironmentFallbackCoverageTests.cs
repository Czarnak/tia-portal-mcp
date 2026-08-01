using System.Reflection;
using TiaMcpServer.Cli.Install;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

[CollectionDefinition("Process environment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
}

[Collection("Process environment")]
public sealed class EnvironmentFallbackCoverageTests
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

    private static ExecutableResolutionResult ResolveCommandClient(string executable)
        => new(
            true,
            executable,
            $@"C:\npm\{executable}.cmd",
            ExecutableKind.CommandScript,
            null);

    [Fact]
    public void BuildProcessArgs_CommandScriptWithoutComspec_UsesSystemCmd()
    {
        var originalComspec = Environment.GetEnvironmentVariable("COMSPEC");
        try
        {
            Environment.SetEnvironmentVariable("COMSPEC", null);
            var command = new NativeCommand(
                "claude",
                new[] { "mcp", "add" },
                false,
                @"C:\npm\claude.cmd",
                ExecutableKind.CommandScript);

            var (fileName, arguments) = NativeProcessRunner.BuildProcessArgs(command);

            Assert.Equal(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "cmd.exe"),
                fileName);
            Assert.Equal("/c", arguments[2]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COMSPEC", originalComspec);
        }
    }

    [Fact]
    public async Task RunAsync_DryRunWithoutComspec_UsesSystemCmdFallback()
    {
        var originalComspec = Environment.GetEnvironmentVariable("COMSPEC");
        try
        {
            Environment.SetEnvironmentVariable("COMSPEC", null);
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = await InstallCommand.RunAsync(
                new[] { "codex", "--dry-run" },
                new FakeProcessRunner(),
                output,
                error,
                ResolveServerExecutable,
                ResolveCommandClient);

            Assert.Equal(0, exitCode);
            Assert.Contains("cmd.exe /d /s /c", output.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("COMSPEC", originalComspec);
        }
    }

    [Fact]
    public void FormatCommand_WithoutResolvedPath_UsesExecutableName()
    {
        var method = typeof(InstallCommand).GetMethod(
            "FormatCommand",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var command = new NativeCommand(
            "codex",
            new[] { "mcp", "list" },
            false,
            null,
            ExecutableKind.Native);

        var formatted = (string[])method.Invoke(null, new object[] { command })!;

        Assert.Equal("codex", formatted[0]);
        Assert.Equal("mcp", formatted[1]);
        Assert.Equal("list", formatted[2]);
    }

    [Fact]
    public void ResolveInstallExecutable_FullResolvedPathMatch_ReusesDetection()
    {
        var method = typeof(InstallCommand).GetMethod(
            "ResolveInstallExecutable",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var detection = new ClientDetectionResult(
            true,
            @"C:\tools\codex.exe",
            ExecutableKind.Native,
            null);
        ExecutableResolutionResult UnexpectedResolver(string executable)
            => throw new InvalidOperationException(
                $"Resolver should not be called for '{executable}'.");

        var result = (ExecutableResolutionResult)method.Invoke(
            null,
            new object[]
            {
                @"C:\tools\codex.exe",
                detection,
                (Func<string, ExecutableResolutionResult>)UnexpectedResolver
            })!;

        Assert.Equal(detection.ExecutablePath, result.ResolvedPath);
        Assert.Equal(detection.Kind, result.Kind);
    }
}
