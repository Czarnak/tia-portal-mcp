using System.Text.Json;
using TiaMcpServer.Diagnostics;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class DoctorJsonRendererTests
{
    [Fact]
    public void Render_ProducesValidJsonWithCorrectStructure()
    {
        var report = CreateReport(DiagnosticStatus.Passed, new[]
        {
            new DiagnosticCheckResult("c1", "OS", DiagnosticStatus.Passed, "Windows")
        });

        var json = DoctorJsonRenderer.Render(report, verbose: false);
        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;
        Assert.Equal("passed", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("timestampUtc", out _));
        Assert.Equal("1.0.0", root.GetProperty("hostVersion").GetString());

        var summary = root.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("passed").GetInt32());
        Assert.Equal(0, summary.GetProperty("warnings").GetInt32());
        Assert.Equal(0, summary.GetProperty("failed").GetInt32());

        var checks = root.GetProperty("checks");
        Assert.Equal(1, checks.GetArrayLength());
        Assert.Equal("c1", checks[0].GetProperty("id").GetString());
        Assert.Equal("passed", checks[0].GetProperty("status").GetString());
    }

    [Fact]
    public void Render_FailedStatus_SerializesCorrectly()
    {
        var report = CreateReport(DiagnosticStatus.Failed, new[]
        {
            new DiagnosticCheckResult("c1", "X", DiagnosticStatus.Failed, "broken")
        });

        var json = DoctorJsonRenderer.Render(report, verbose: false);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("failed", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void Render_Verbose_IncludesEvidence()
    {
        var evidence = new Dictionary<string, string?> { ["k"] = "v" };
        var report = CreateReport(DiagnosticStatus.Passed, new[]
        {
            new DiagnosticCheckResult("c1", "X", DiagnosticStatus.Passed, "msg", Evidence: evidence)
        });

        var json = DoctorJsonRenderer.Render(report, verbose: true);
        using var doc = JsonDocument.Parse(json);

        var check = doc.RootElement.GetProperty("checks")[0];
        Assert.True(check.TryGetProperty("evidence", out var ev));
        Assert.Equal("v", ev.GetProperty("k").GetString());
    }

    [Fact]
    public void Render_NonVerbose_OmitsEvidence()
    {
        var evidence = new Dictionary<string, string?> { ["k"] = "v" };
        var report = CreateReport(DiagnosticStatus.Passed, new[]
        {
            new DiagnosticCheckResult("c1", "X", DiagnosticStatus.Passed, "msg", Evidence: evidence)
        });

        var json = DoctorJsonRenderer.Render(report, verbose: false);
        using var doc = JsonDocument.Parse(json);

        var check = doc.RootElement.GetProperty("checks")[0];
        Assert.False(check.TryGetProperty("evidence", out _));
    }

    [Fact]
    public void Render_NullRemediation_SerializesAsNull()
    {
        var report = CreateReport(DiagnosticStatus.Passed, new[]
        {
            new DiagnosticCheckResult("c1", "X", DiagnosticStatus.Passed, "msg")
        });

        var json = DoctorJsonRenderer.Render(report, verbose: false);
        using var doc = JsonDocument.Parse(json);

        var check = doc.RootElement.GetProperty("checks")[0];
        Assert.True(check.GetProperty("remediation").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public void Render_NonNullRemediation_SerializesAsString()
    {
        var report = CreateReport(DiagnosticStatus.Failed, new[]
        {
            new DiagnosticCheckResult("c1", "X", DiagnosticStatus.Failed, "msg", "Fix it")
        });

        var json = DoctorJsonRenderer.Render(report, verbose: false);
        using var doc = JsonDocument.Parse(json);

        var check = doc.RootElement.GetProperty("checks")[0];
        Assert.Equal("Fix it", check.GetProperty("remediation").GetString());
    }

    [Fact]
    public void Render_EvidenceNullValue_SerializesAsNull()
    {
        var evidence = new Dictionary<string, string?> { ["k"] = null };
        var report = CreateReport(DiagnosticStatus.Passed, new[]
        {
            new DiagnosticCheckResult("c1", "X", DiagnosticStatus.Passed, "msg", Evidence: evidence)
        });

        var json = DoctorJsonRenderer.Render(report, verbose: true);
        using var doc = JsonDocument.Parse(json);

        var ev = doc.RootElement.GetProperty("checks")[0].GetProperty("evidence");
        Assert.True(ev.GetProperty("k").ValueKind == JsonValueKind.Null);
    }

    private static DoctorReport CreateReport(DiagnosticStatus status, DiagnosticCheckResult[] checks)
        => new(status, DateTimeOffset.UtcNow, "1.0.0",
            new(checks.Count(c => c.Status == DiagnosticStatus.Passed),
                checks.Count(c => c.Status == DiagnosticStatus.Warning),
                checks.Count(c => c.Status == DiagnosticStatus.Failed)),
            checks);
}
