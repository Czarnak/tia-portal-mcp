using System.Reflection;
using TiaMcpServer.Cli.Install;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

public sealed class InstallCommandEntryPointCoverageTests
{
    private sealed class FakeProcessRunner : INativeProcessRunner
    {
        public Task<NativeCommandResult> RunAsync(
            NativeCommand command,
            CancellationToken cancellationToken)
            => Task.FromResult(new NativeCommandResult(0, string.Empty, string.Empty));
    }

    [Fact]
    public async Task RunAsync_InjectedRunnerEntryPoint_HandlesHelp()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await InstallCommand.RunAsync(
            new[] { "--help" },
            new FakeProcessRunner(),
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage: tia-mcp install", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void FormatClientName_UnknownValue_UsesEnumText()
    {
        var method = typeof(InstallCommand).GetMethod(
            "FormatClientName",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var formatted = method.Invoke(null, new object[] { (ClientKind)12345 });

        Assert.Equal("12345", formatted);
    }
}
