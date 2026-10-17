using System.Text.Json;
using TiaMcpServer.Cli;
using TiaMcpServer.Diagnostics;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class DoctorCommandTests
{
    [Fact]
    public async Task RunAsync_InvalidWorkerTimeout_ReturnsTwoWithSharedErrorWithoutRunningChecks()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DoctorCommand.RunAsync(
            new[] { "--worker-timeout-minutes", "61" },
            _ => throw new InvalidOperationException("Checks must not run for invalid arguments."),
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Empty(output.ToString());
        Assert.StartsWith($"error: Invalid --worker-timeout-minutes value '61'. Expected a whole number of minutes from 5 to 60.{Environment.NewLine}", error.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_WorkerTimeout_ReportsResolvedValueAndSource(bool json)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var args = json
            ? new[] { "--json", "--worker-timeout-minutes", "10" }
            : new[] { "--worker-timeout-minutes", "10" };

        var exitCode = await DoctorCommand.RunAsync(args, _ => PassedReport(), output, error);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        if (json)
        {
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal(10, document.RootElement.GetProperty("workerTimeoutMinutes").GetInt32());
            Assert.Equal("option", document.RootElement.GetProperty("workerTimeoutSource").GetString());
        }
        else
        {
            Assert.Contains("Worker timeout: 10 minutes (option)", output.ToString());
        }
    }

    [Fact]
    public async Task RunAsync_Help_WritesUsageToStandardOutputAndReturnsZero()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DoctorCommand.RunAsync(
            new[] { "--help" },
            _ => PassedReport(),
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage: tia-mcp doctor", output.ToString());
        Assert.Contains("--worker-timeout-minutes", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Theory]
    [InlineData("--bogus", "--help")]
    [InlineData("--help", "--bogus")]
    [InlineData("--worker-timeout-minutes", "61", "--help")]
    [InlineData("--help", "--access-mode", "nope")]
    [InlineData("--read-only", "--read-write", "--help")]
    [InlineData("--project", "--help")]
    public async Task RunAsync_HelpWithInvalidArguments_WritesUsageAndReturnsZero(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DoctorCommand.RunAsync(
            args,
            _ => throw new InvalidOperationException("Checks must not run for --help."),
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage: tia-mcp doctor", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task RunAsync_JsonHelpWithInvalidArguments_WritesUsageDocument()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DoctorCommand.RunAsync(
            new[] { "--json", "--bogus", "--help" },
            _ => throw new InvalidOperationException("Checks must not run for --help."),
            output,
            error);

        using var document = JsonDocument.Parse(output.ToString());

        Assert.Equal(0, exitCode);
        Assert.True(document.RootElement.TryGetProperty("usage", out _));
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task RunAsync_JsonHelp_WritesOneParseableUsageDocumentToStandardOutput()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DoctorCommand.RunAsync(
            new[] { "--json", "--help" },
            _ => PassedReport(),
            output,
            error);

        using var document = JsonDocument.Parse(output.ToString());

        Assert.Equal(0, exitCode);
        Assert.True(document.RootElement.TryGetProperty("usage", out var usage));
        Assert.Contains("--help", usage.GetString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task RunAsync_JsonParseError_WritesParseableErrorToStandardOutput()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DoctorCommand.RunAsync(
            new[] { "--json", "--project=" },
            _ => PassedReport(),
            output,
            error);

        using var document = JsonDocument.Parse(output.ToString());

        Assert.Equal(2, exitCode);
        Assert.True(document.RootElement.TryGetProperty("error", out var errorElement));
        Assert.Contains("requires a value", errorElement.GetString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task RunAsync_JsonProjectWithSeparateEmptyValue_WritesOneErrorDocumentAndReturnsTwo()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DoctorCommand.RunAsync(
            new[] { "--json", "--project", "" },
            _ => PassedReport(),
            output,
            error);

        using var document = JsonDocument.Parse(output.ToString());

        Assert.Equal(2, exitCode);
        Assert.Contains("requires a value", document.RootElement.GetProperty("error").GetString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task RunAsync_ProjectThenJson_WritesParseableErrorToStandardOutput()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DoctorCommand.RunAsync(
            new[] { "--project", "--json" },
            _ => PassedReport(),
            output,
            error);

        using var document = JsonDocument.Parse(output.ToString());

        Assert.Equal(2, exitCode);
        Assert.Contains("requires a value", document.RootElement.GetProperty("error").GetString());
        Assert.Empty(error.ToString());
    }

    [Theory]
    [InlineData(DiagnosticStatus.Passed, false, 0)]
    [InlineData(DiagnosticStatus.Warning, false, 0)]
    [InlineData(DiagnosticStatus.Failed, false, 1)]
    [InlineData(DiagnosticStatus.Failed, true, 2)]
    public async Task RunAsync_MapsReportStateToExpectedExitCode(
        DiagnosticStatus status,
        bool hasUnexpectedCheckFailure,
        int expectedExitCode)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var report = new DoctorReport(
            status,
            DateTimeOffset.UtcNow,
            "1.0.0",
            new DoctorSummary(0, 0, status == DiagnosticStatus.Failed ? 1 : 0),
            Array.Empty<DiagnosticCheckResult>())
        {
            HasUnexpectedCheckFailure = hasUnexpectedCheckFailure
        };

        var exitCode = await DoctorCommand.RunAsync(
            Array.Empty<string>(),
            _ => report,
            output,
            error);

        Assert.Equal(expectedExitCode, exitCode);
        Assert.Empty(error.ToString());
    }

    private static DoctorReport PassedReport() => new(
        DiagnosticStatus.Passed,
        DateTimeOffset.UtcNow,
        "1.0.0",
        new DoctorSummary(0, 0, 0),
        Array.Empty<DiagnosticCheckResult>());
}
