using TiaMcpServer.Diagnostics;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class DoctorTextRendererTests
{
    [Fact]
    public void Render_AllPassed_ShowsPassTagsAndReady()
    {
        var report = CreateReport(DiagnosticStatus.Passed, new[]
        {
            new DiagnosticCheckResult("c1", "OS", DiagnosticStatus.Passed, "Windows 10"),
            new DiagnosticCheckResult("c2", "Runtime", DiagnosticStatus.Passed, ".NET 8")
        });

        var writer = new StringWriter();
        DoctorTextRenderer.Render(report, verbose: false, writer);
        var output = writer.ToString();

        Assert.Contains("[PASS] OS", output);
        Assert.Contains("[PASS] Runtime", output);
        Assert.Contains("2 passed", output);
        Assert.Contains("Environment is ready", output);
    }

    [Fact]
    public void Render_WithFailure_ShowsFailTagsAndNotReady()
    {
        var report = CreateReport(DiagnosticStatus.Failed, new[]
        {
            new DiagnosticCheckResult("c1", "OS", DiagnosticStatus.Passed, "Windows 10"),
            new DiagnosticCheckResult("c2", "Install", DiagnosticStatus.Failed, "Not found", "Install TIA Portal")
        });

        var writer = new StringWriter();
        DoctorTextRenderer.Render(report, verbose: false, writer);
        var output = writer.ToString();

        Assert.Contains("[PASS] OS", output);
        Assert.Contains("[FAIL] Install", output);
        Assert.Contains("Not found", output);
        Assert.Contains("Environment is not ready", output);
    }

    [Fact]
    public void Render_WithWarning_ShowsWarnTag()
    {
        var report = CreateReport(DiagnosticStatus.Passed, new[]
        {
            new DiagnosticCheckResult("c1", "Process", DiagnosticStatus.Warning, "No TIA running")
        });

        var writer = new StringWriter();
        DoctorTextRenderer.Render(report, verbose: false, writer);
        var output = writer.ToString();

        Assert.Contains("[WARN] Process", output);
        Assert.Contains("1 warning", output);
    }

    [Fact]
    public void Render_Verbose_ShowsEvidence()
    {
        var evidence = new Dictionary<string, string?> { ["key1"] = "val1", ["key2"] = null };
        var report = CreateReport(DiagnosticStatus.Passed, new[]
        {
            new DiagnosticCheckResult("c1", "Test", DiagnosticStatus.Passed, "msg", Evidence: evidence)
        });

        var writer = new StringWriter();
        DoctorTextRenderer.Render(report, verbose: true, writer);
        var output = writer.ToString();

        Assert.Contains("Evidence:", output);
        Assert.Contains("key1 = val1", output);
        Assert.Contains("key2 = <null>", output);
    }

    [Fact]
    public void Render_NonVerbose_HidesEvidence()
    {
        var evidence = new Dictionary<string, string?> { ["key1"] = "val1" };
        var report = CreateReport(DiagnosticStatus.Passed, new[]
        {
            new DiagnosticCheckResult("c1", "Test", DiagnosticStatus.Passed, "msg", Evidence: evidence)
        });

        var writer = new StringWriter();
        DoctorTextRenderer.Render(report, verbose: false, writer);
        var output = writer.ToString();

        Assert.DoesNotContain("Evidence:", output);
        Assert.DoesNotContain("key1", output);
    }

    [Fact]
    public void Render_WithRemediation_ShowsFix()
    {
        var report = CreateReport(DiagnosticStatus.Failed, new[]
        {
            new DiagnosticCheckResult("c1", "Install", DiagnosticStatus.Failed, "Missing", "Install TIA Portal V21.")
        });

        var writer = new StringWriter();
        DoctorTextRenderer.Render(report, verbose: false, writer);
        var output = writer.ToString();

        Assert.Contains("Fix:", output);
        Assert.Contains("Install TIA Portal V21.", output);
    }

    [Fact]
    public void Render_MultiLineRemediation_IndentsEachLine()
    {
        var report = CreateReport(DiagnosticStatus.Failed, new[]
        {
            new DiagnosticCheckResult("c1", "X", DiagnosticStatus.Failed, "msg", "Line one\nLine two")
        });

        var writer = new StringWriter();
        DoctorTextRenderer.Render(report, verbose: false, writer);
        var output = writer.ToString();

        Assert.Contains("       Line one", output);
        Assert.Contains("       Line two", output);
    }

    private static DoctorReport CreateReport(DiagnosticStatus status, DiagnosticCheckResult[] checks)
        => new(status, DateTimeOffset.UtcNow, "1.0.0",
            new(checks.Count(c => c.Status == DiagnosticStatus.Passed),
                checks.Count(c => c.Status == DiagnosticStatus.Warning),
                checks.Count(c => c.Status == DiagnosticStatus.Failed)),
            checks);
}
