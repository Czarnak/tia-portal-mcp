using System.Reflection;
using TiaMcpServer.Cli.Install;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

public sealed class ExecutableResolutionCoverageTests
{
    private sealed class FakeProcessRunner : INativeProcessRunner
    {
        public List<NativeCommand> ExecutedCommands { get; } = new();
        public Queue<NativeCommandResult> Results { get; } = new();

        public Task<NativeCommandResult> RunAsync(
            NativeCommand command,
            CancellationToken cancellationToken)
        {
            ExecutedCommands.Add(command);
            return Task.FromResult(
                Results.Count > 0
                    ? Results.Dequeue()
                    : new NativeCommandResult(0, string.Empty, string.Empty));
        }
    }

    private static string? ResolveServerExecutable(string? path)
        => path ?? @"C:\tools\tia-mcp.exe";

    private static ExecutableResolutionResult ResolveNativeClient(string executable)
        => new(
            true,
            executable,
            $@"C:\tools\{executable}.exe",
            ExecutableKind.Native,
            null);

    private static ExecutableResolutionResult ResolveBatchClient(string executable)
        => new(
            true,
            executable,
            $@"C:\npm\{executable}.bat",
            ExecutableKind.BatchScript,
            null);

    [Theory]
    [InlineData(".exe", ExecutableKind.Native)]
    [InlineData(".cmd", ExecutableKind.CommandScript)]
    [InlineData(".bat", ExecutableKind.BatchScript)]
    public void ResolveClientExecutable_AbsolutePathWithoutExtension_ResolvesFile(
        string extension,
        ExecutableKind expectedKind)
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "tia_mcp_absolute_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        var commandPath = Path.Combine(tempDirectory, "client");
        var executablePath = commandPath + extension;
        File.WriteAllText(executablePath, string.Empty);

        try
        {
            var result = ExecutableResolver.ResolveClientExecutable(commandPath);

            Assert.True(result.Found);
            Assert.Equal(Path.GetFullPath(executablePath), result.ResolvedPath);
            Assert.Equal(expectedKind, result.Kind);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData(".exe", ExecutableKind.Native)]
    [InlineData(".cmd", ExecutableKind.CommandScript)]
    [InlineData(".bat", ExecutableKind.BatchScript)]
    public void ResolveClientExecutable_CommonProgramsDirectory_ResolvesFile(
        string extension,
        ExecutableKind expectedKind)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var programsDirectory = Path.Combine(localAppData, "Programs");
        Directory.CreateDirectory(programsDirectory);

        var command = "tia_mcp_common_" + Guid.NewGuid().ToString("N");
        var executablePath = Path.Combine(programsDirectory, command + extension);
        File.WriteAllText(executablePath, string.Empty);

        try
        {
            var result = ExecutableResolver.ResolveClientExecutable(command);

            Assert.True(result.Found);
            Assert.Equal(executablePath, result.ResolvedPath);
            Assert.Equal(expectedKind, result.Kind);
        }
        finally
        {
            File.Delete(executablePath);
        }
    }

    [Fact]
    public void ResolveClientExecutable_CommonProgramsDirectory_ResolvesBareFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var programsDirectory = Path.Combine(localAppData, "Programs");
        Directory.CreateDirectory(programsDirectory);

        var command = "tia_mcp_bare_" + Guid.NewGuid().ToString("N");
        var executablePath = Path.Combine(programsDirectory, command);
        File.WriteAllText(executablePath, string.Empty);

        try
        {
            var result = ExecutableResolver.ResolveClientExecutable(command);

            Assert.True(result.Found);
            Assert.Equal(executablePath, result.ResolvedPath);
            Assert.Equal(ExecutableKind.Native, result.Kind);
        }
        finally
        {
            File.Delete(executablePath);
        }
    }

    [Theory]
    [InlineData(@"C:\tools\client.exe", 0)]
    [InlineData(@"C:\tools\client.cmd", 1)]
    [InlineData(@"C:\tools\client.bat", 2)]
    [InlineData(@"C:\tools\client", 3)]
    public void ExtensionPriority_OrdersSupportedExecutableKinds(
        string path,
        int expectedPriority)
    {
        var method = typeof(ExecutableResolver).GetMethod(
            "ExtensionPriority",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var priority = (int)method.Invoke(null, new object[] { path })!;
        Assert.Equal(expectedPriority, priority);
    }

    [Fact]
    public async Task RunAsync_ParseErrorWithJson_EmitsStructuredError()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            new[] { "--json", "unsupported-client" },
            runner,
            output,
            error,
            ResolveServerExecutable,
            ResolveNativeClient);

        Assert.Equal(3, exitCode);
        Assert.Contains("\"success\":false", output.ToString());
        Assert.Contains("Unsupported MCP client", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task RunAsync_MiMoDryRun_PrintsInteractiveGuideWithoutExecuting()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            new[] { "mimo", "--dry-run" },
            runner,
            output,
            error,
            ResolveServerExecutable,
            ResolveNativeClient);

        Assert.Equal(0, exitCode);
        Assert.Empty(runner.ExecutedCommands);
        Assert.Contains("Interactive mode: follow the prompts below.", output.ToString());
        Assert.Contains("Server name:     tia-portal", output.ToString());
        Assert.Contains("Transport type:  stdio", output.ToString());
    }

    [Fact]
    public async Task RunAsync_InstallFailureWithOnlyStdout_WritesStdout()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(new NativeCommandResult(1, "failure on stdout", string.Empty));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            new[] { "codex" },
            runner,
            output,
            error,
            ResolveServerExecutable,
            ResolveNativeClient);

        Assert.Equal(6, exitCode);
        Assert.Contains("failure on stdout", error.ToString());
    }

    [Fact]
    public async Task RunAsync_InstallFailureJsonWithOnlyStdout_UsesStdoutAsError()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(new NativeCommandResult(1, "failure on stdout", "   "));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            new[] { "codex", "--json" },
            runner,
            output,
            error,
            ResolveServerExecutable,
            ResolveNativeClient);

        Assert.Equal(6, exitCode);
        Assert.Contains("\"error\":\"failure on stdout\"", output.ToString());
    }

    [Fact]
    public async Task RunAsync_DryRunJsonWithBatchShim_ReportsActualExecution()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            new[] { "codex", "--dry-run", "--json" },
            runner,
            output,
            error,
            ResolveServerExecutable,
            ResolveBatchClient);

        var json = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("\"clientExecutableKind\":\"batch_script\"", json);
        Assert.Contains("codex.bat", json);
        Assert.Contains("\"/c\"", json);
    }

    [Fact]
    public async Task RunAsync_OpenCodeDryRunJson_ReportsClientCommand()
    {
        var runner = new FakeProcessRunner();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            new[] { "opencode", "--dry-run", "--json" },
            runner,
            output,
            error,
            ResolveServerExecutable,
            ResolveNativeClient);

        Assert.Equal(0, exitCode);
        Assert.Contains("\"clientCommand\":\"opencode\"", output.ToString());
    }

    [Fact]
    public void ResolveInstallExecutable_DifferentCommand_UsesResolver()
    {
        var method = typeof(InstallCommand).GetMethod(
            "ResolveInstallExecutable",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var detection = new ClientDetectionResult(
            true,
            @"C:\tools\mimo.exe",
            ExecutableKind.Native,
            null);
        string? resolvedCommand = null;
        ExecutableResolutionResult Resolver(string executable)
        {
            resolvedCommand = executable;
            return new ExecutableResolutionResult(
                true,
                executable,
                @"C:\npm\claude.cmd",
                ExecutableKind.CommandScript,
                null);
        }

        var result = (ExecutableResolutionResult)method.Invoke(
            null,
            new object[]
            {
                "claude",
                detection,
                (Func<string, ExecutableResolutionResult>)Resolver
            })!;

        Assert.Equal("claude", resolvedCommand);
        Assert.Equal(@"C:\npm\claude.cmd", result.ResolvedPath);
        Assert.Equal(ExecutableKind.CommandScript, result.Kind);
    }

    [Fact]
    public void PrintInteractiveGuide_UnsupportedClient_PrintsFallback()
    {
        var method = typeof(InstallCommand).GetMethod(
            "PrintInteractiveGuide",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var output = new StringWriter();
        var spec = new McpLaunchSpec(
            "tia-portal",
            @"C:\tools\tia-mcp.exe",
            new[] { "--access-mode", "read-only" });
        var options = new InstallOptions(
            true,
            ClientKind.Codex,
            "tia-portal",
            "read-only",
            null,
            null,
            false,
            false,
            false,
            null);

        method.Invoke(null, new object[] { output, ClientKind.Codex, spec, options });

        Assert.Contains("no guide available for Codex", output.ToString());
    }

    [Fact]
    public void GetClientCommand_FormatsMiMoAndUnknownValues()
    {
        var method = typeof(InstallCommand).GetMethod(
            "GetClientCommand",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        Assert.Equal("mimo", method.Invoke(null, new object[] { ClientKind.MiMoCode }));
        Assert.Equal("12345", method.Invoke(null, new object[] { (ClientKind)12345 }));
    }

    [Fact]
    public void FormatKind_FormatsBatchAndUnknownValues()
    {
        var method = typeof(InstallCommand).GetMethod(
            "FormatKind",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        Assert.Equal(
            "batch_script",
            method.Invoke(null, new object[] { ExecutableKind.BatchScript }));
        Assert.Equal(
            "12345",
            method.Invoke(null, new object[] { (ExecutableKind)12345 }));
    }
}
