using TiaMcpServer.Diagnostics;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class DoctorRunnerTests
{
    [Fact]
    public void Run_AllChecksPass_ReturnsPassedStatus()
    {
        var appInfo = new FakeApplicationInfoService();
        var checks = new IDiagnosticCheck[]
        {
            new StubCheck("c1", "Check 1", DiagnosticStatus.Passed),
            new StubCheck("c2", "Check 2", DiagnosticStatus.Passed)
        };

        var runner = new DoctorRunner(appInfo, checks);
        var report = runner.Run();

        Assert.Equal(DiagnosticStatus.Passed, report.Status);
        Assert.Equal(2, report.Summary.Passed);
        Assert.Equal(0, report.Summary.Warnings);
        Assert.Equal(0, report.Summary.Failed);
        Assert.Equal(2, report.Checks.Count);
    }

    [Fact]
    public void Run_MixedResults_ReturnsFailedIfAnyFailed()
    {
        var appInfo = new FakeApplicationInfoService();
        var checks = new IDiagnosticCheck[]
        {
            new StubCheck("c1", "Check 1", DiagnosticStatus.Passed),
            new StubCheck("c2", "Check 2", DiagnosticStatus.Warning),
            new StubCheck("c3", "Check 3", DiagnosticStatus.Failed)
        };

        var runner = new DoctorRunner(appInfo, checks);
        var report = runner.Run();

        Assert.Equal(DiagnosticStatus.Failed, report.Status);
        Assert.Equal(1, report.Summary.Passed);
        Assert.Equal(1, report.Summary.Warnings);
        Assert.Equal(1, report.Summary.Failed);
    }

    [Fact]
    public void Run_OnlyWarnings_ReturnsWarning()
    {
        var appInfo = new FakeApplicationInfoService();
        var checks = new IDiagnosticCheck[]
        {
            new StubCheck("c1", "Check 1", DiagnosticStatus.Warning),
            new StubCheck("c2", "Check 2", DiagnosticStatus.Warning)
        };

        var runner = new DoctorRunner(appInfo, checks);
        var report = runner.Run();

        Assert.Equal(DiagnosticStatus.Warning, report.Status);
        Assert.Equal(2, report.Summary.Warnings);
    }

    [Fact]
    public void Run_ThrowingCheck_CapturedAsFailed()
    {
        var appInfo = new FakeApplicationInfoService();
        var checks = new IDiagnosticCheck[]
        {
            new StubCheck("c1", "Check 1", DiagnosticStatus.Passed),
            new ThrowingCheck(),
            new StubCheck("c3", "Check 3", DiagnosticStatus.Passed)
        };

        var runner = new DoctorRunner(appInfo, checks);
        var report = runner.Run();

        Assert.Equal(DiagnosticStatus.Failed, report.Status);
        Assert.Equal(3, report.Checks.Count);
        Assert.Equal(DiagnosticStatus.Failed, report.Checks[1].Status);
        Assert.Contains("Unexpected error", report.Checks[1].Message);
    }

    [Fact]
    public void Run_EmptyCheckList_ReturnsPassed()
    {
        var appInfo = new FakeApplicationInfoService();
        var runner = new DoctorRunner(appInfo, Array.Empty<IDiagnosticCheck>());
        var report = runner.Run();

        Assert.Equal(DiagnosticStatus.Passed, report.Status);
        Assert.Empty(report.Checks);
        Assert.Equal(0, report.Summary.Passed);
    }

    [Fact]
    public void Run_IncludesHostVersionFromAppInfo()
    {
        var appInfo = new FakeApplicationInfoService { HostVersion = "2.5.0" };
        var runner = new DoctorRunner(appInfo, Array.Empty<IDiagnosticCheck>());
        var report = runner.Run();

        Assert.Equal("2.5.0", report.HostVersion);
    }

    [Fact]
    public void Run_SetsTimestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var appInfo = new FakeApplicationInfoService();
        var runner = new DoctorRunner(appInfo, Array.Empty<IDiagnosticCheck>());
        var report = runner.Run();
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(report.TimestampUtc, before, after);
    }

    private sealed class StubCheck : IDiagnosticCheck
    {
        private readonly DiagnosticStatus _status;
        public StubCheck(string id, string name, DiagnosticStatus status) { Id = id; Name = name; _status = status; }
        public string Id { get; }
        public string Name { get; }
        public DiagnosticCheckResult Run() => new(Id, Name, _status, $"{Name} message");
    }

    private sealed class ThrowingCheck : IDiagnosticCheck
    {
        public string Id => "throwing";
        public string Name => "Throwing Check";
        public DiagnosticCheckResult Run() => throw new InvalidOperationException("boom");
    }
}
