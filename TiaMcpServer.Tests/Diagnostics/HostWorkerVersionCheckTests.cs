using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class HostWorkerVersionCheckTests
{
    [Fact]
    public void WorkerNotFound_ReturnsWarning()
    {
        var appInfo = new FakeApplicationInfoService { BaseDirectory = "/app", HostVersion = "1.0.0" };
        var fileSystem = new FakeFileSystemService();
        // No worker found

        var check = new HostWorkerVersionCheck(appInfo, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("not found", result.Message);
    }

    [Fact]
    public void MatchingMajorVersions_ReturnsPassed()
    {
        var appInfo = new FakeApplicationInfoService { BaseDirectory = "/app", HostVersion = "1.2.3" };
        var fileSystem = new FakeFileSystemService();
        var workerPath = System.IO.Path.Combine("/app", "openness-worker", "TiaMcpServer.OpennessWorker.exe");
        fileSystem.AddFile(workerPath);
        fileSystem.SetFileVersion(workerPath, "1.5.0.0");

        var check = new HostWorkerVersionCheck(appInfo, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("1.2.3", result.Message);
        Assert.Contains("1.5.0.0", result.Message);
    }

    [Fact]
    public void DifferentMajorVersions_ReturnsFailed()
    {
        var appInfo = new FakeApplicationInfoService { BaseDirectory = "/app", HostVersion = "1.0.0" };
        var fileSystem = new FakeFileSystemService();
        var workerPath = System.IO.Path.Combine("/app", "openness-worker", "TiaMcpServer.OpennessWorker.exe");
        fileSystem.AddFile(workerPath);
        fileSystem.SetFileVersion(workerPath, "2.0.0.0");

        var check = new HostWorkerVersionCheck(appInfo, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("different major versions", result.Message);
        Assert.NotNull(result.Remediation);
    }

    [Fact]
    public void WorkerVersionNull_ReturnsWarning()
    {
        var appInfo = new FakeApplicationInfoService { BaseDirectory = "/app", HostVersion = "1.0.0" };
        var fileSystem = new FakeFileSystemService();
        var workerPath = System.IO.Path.Combine("/app", "openness-worker", "TiaMcpServer.OpennessWorker.exe");
        fileSystem.AddFile(workerPath);
        // No version set -> null

        var check = new HostWorkerVersionCheck(appInfo, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("could not be determined", result.Message);
    }
}
