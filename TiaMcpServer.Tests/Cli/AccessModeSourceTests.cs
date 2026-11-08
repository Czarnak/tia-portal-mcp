using System.Text.Json;
using TiaMcpServer.Cli;
using TiaMcpServer.Diagnostics;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

[Collection("Process environment")]
public class AccessModeSourceTests
{
    public static IEnumerable<object[]> Sources => new object[][]
    {
        [null!, "read-only", "default", Array.Empty<string>()],
        ["full", "full", "environment", Array.Empty<string>()],
        ["read-only", "read-only", "environment", Array.Empty<string>()],
        ["invalid", "read-only", "option", new[] { "--read-only" }],
        ["full", "read-write", "option", new[] { "--read-write" }],
        ["read-only", "full", "option", new[] { "--access-mode", "full" }],
        ["full", "read-write", "option", new[] { "--access-mode=read-write" }]
    };

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task Startup_ReportsSourceAndHintsOnlyForDefault(
        string? environmentValue, string mode, string source, string[] args)
    {
        var environment = environmentValue is null ? null : new Dictionary<string, string>
        {
            ["TIA_MCP_ACCESS_MODE"] = environmentValue
        };
        var result = await StartupProcess.RunAsync(environment, args);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"TIA MCP access mode: {mode.ToUpperInvariant()} ({source})", result.StandardError);
        Assert.Empty(result.StandardOutput);
        Assert.Equal(source == "default", result.StandardError.Contains("No access mode was given."));
        if (source == "default")
        {
            Assert.Contains("--access-mode read-write", result.StandardError);
            Assert.Contains("--access-mode full", result.StandardError);
            Assert.Contains("TIA_MCP_ACCESS_MODE", result.StandardError);
        }
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task Doctor_ReportsResolvedModeAndSourceInTextAndJson(
        string? environmentValue, string mode, string source, string[] args)
    {
        var previous = Environment.GetEnvironmentVariable("TIA_MCP_ACCESS_MODE");
        try
        {
            Environment.SetEnvironmentVariable("TIA_MCP_ACCESS_MODE", environmentValue);
            foreach (var json in new[] { false, true })
            {
                using var output = new StringWriter();
                using var error = new StringWriter();
                var exitCode = await DoctorCommand.RunAsync(
                    json ? args.Append("--json").ToArray() : args,
                    _ => new DoctorReport(DiagnosticStatus.Passed, DateTimeOffset.UtcNow, "1.0.0",
                        new DoctorSummary(0, 0, 0), Array.Empty<DiagnosticCheckResult>()),
                    output, error);

                Assert.Equal(0, exitCode);
                Assert.Empty(error.ToString());
                if (json)
                {
                    using var document = JsonDocument.Parse(output.ToString());
                    Assert.Equal(mode, document.RootElement.GetProperty("accessMode").GetString());
                    Assert.Equal(source, document.RootElement.GetProperty("accessModeSource").GetString());
                }
                else
                {
                    Assert.Contains($"Access mode: {mode.ToUpperInvariant()} ({source})", output.ToString());
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIA_MCP_ACCESS_MODE", previous);
        }
    }
}
