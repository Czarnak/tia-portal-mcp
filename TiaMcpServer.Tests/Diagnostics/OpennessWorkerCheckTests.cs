using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class OpennessWorkerCheckTests
{
    [Fact]
    public void WorkerFoundWithRuntimeConfig_ReturnsPassed()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "doctor-test-" + Guid.NewGuid().ToString("N"));
        var workerDir = Path.Combine(baseDir, "openness-worker");
        var workerPath = Path.Combine(workerDir, "TiaMcpServer.OpennessWorker.exe");
        var runtimeConfigPath = Path.Combine(workerDir, "TiaMcpServer.OpennessWorker.runtimeconfig.json");

        var appInfo = new FakeApplicationInfoService { BaseDirectory = baseDir };
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddFile(workerPath);
        fileSystem.AddFile(runtimeConfigPath);
        fileSystem.SetFileVersion(workerPath, "1.0.0.0");

        var check = new OpennessWorkerCheck(appInfo, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("1.0.0.0", result.Message);
    }

    [Fact]
    public void WorkerNotFound_ReturnsFailed()
    {
        var appInfo = new FakeApplicationInfoService { BaseDirectory = Path.Combine(Path.GetTempPath(), "nonexistent") };
        var fileSystem = new FakeFileSystemService();

        var check = new OpennessWorkerCheck(appInfo, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("not found", result.Message);
        Assert.NotNull(result.Remediation);
    }

    [Fact]
    public void WorkerFoundButRuntimeConfigMissing_ReturnsFailed()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "doctor-test-" + Guid.NewGuid().ToString("N"));
        var workerDir = Path.Combine(baseDir, "openness-worker");
        var workerPath = Path.Combine(workerDir, "TiaMcpServer.OpennessWorker.exe");

        var appInfo = new FakeApplicationInfoService { BaseDirectory = baseDir };
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddFile(workerPath);

        var check = new OpennessWorkerCheck(appInfo, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("runtime configuration", result.Message);
    }

    [Fact]
    public void WorkerFoundWithoutVersion_PassesWithVersionlessMessage()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "doctor-test-" + Guid.NewGuid().ToString("N"));
        var workerDir = Path.Combine(baseDir, "openness-worker");
        var workerPath = Path.Combine(workerDir, "TiaMcpServer.OpennessWorker.exe");
        var runtimeConfigPath = Path.Combine(workerDir, "TiaMcpServer.OpennessWorker.runtimeconfig.json");

        var appInfo = new FakeApplicationInfoService { BaseDirectory = baseDir };
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddFile(workerPath);
        fileSystem.AddFile(runtimeConfigPath);

        var check = new OpennessWorkerCheck(appInfo, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.DoesNotContain("version", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
