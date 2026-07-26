using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class OpennessWorkerCheckTests
{
    private static readonly string[] RequiredCompanionFiles =
    {
        "TiaMcpServer.OpennessWorker.exe.config",
        "TiaMcpServer.Contracts.dll",
        "Microsoft.Bcl.AsyncInterfaces.dll",
        "System.Buffers.dll",
        "System.IO.Pipelines.dll",
        "System.Memory.dll",
        "System.Numerics.Vectors.dll",
        "System.Runtime.CompilerServices.Unsafe.dll",
        "System.Text.Encodings.Web.dll",
        "System.Text.Json.dll",
        "System.Threading.Tasks.Extensions.dll"
    };

    [Fact]
    public void WorkerFoundWithRequiredCompanionFiles_ReturnsPassed()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "doctor-test-" + Guid.NewGuid().ToString("N"));
        var workerDir = Path.Combine(baseDir, "openness-worker");
        var workerPath = Path.Combine(workerDir, "TiaMcpServer.OpennessWorker.exe");

        var appInfo = new FakeApplicationInfoService { BaseDirectory = baseDir };
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddFile(workerPath);
        AddRequiredCompanionFiles(fileSystem, workerDir);
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
    public void WorkerFoundButContractsAssemblyMissing_ReturnsFailed()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "doctor-test-" + Guid.NewGuid().ToString("N"));
        var workerDir = Path.Combine(baseDir, "openness-worker");
        var workerPath = Path.Combine(workerDir, "TiaMcpServer.OpennessWorker.exe");

        var appInfo = new FakeApplicationInfoService { BaseDirectory = baseDir };
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddFile(workerPath);
        AddRequiredCompanionFiles(fileSystem, workerDir, "TiaMcpServer.Contracts.dll");

        var check = new OpennessWorkerCheck(appInfo, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("TiaMcpServer.Contracts.dll", result.Message);
    }

    [Fact]
    public void WorkerFoundWithoutVersion_PassesWithVersionlessMessage()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "doctor-test-" + Guid.NewGuid().ToString("N"));
        var workerDir = Path.Combine(baseDir, "openness-worker");
        var workerPath = Path.Combine(workerDir, "TiaMcpServer.OpennessWorker.exe");

        var appInfo = new FakeApplicationInfoService { BaseDirectory = baseDir };
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddFile(workerPath);
        AddRequiredCompanionFiles(fileSystem, workerDir);

        var check = new OpennessWorkerCheck(appInfo, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.DoesNotContain("version", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddRequiredCompanionFiles(
        FakeFileSystemService fileSystem,
        string workerDir,
        string? excludedFile = null)
    {
        foreach (var companionFile in RequiredCompanionFiles)
        {
            if (!string.Equals(companionFile, excludedFile, StringComparison.Ordinal))
            {
                fileSystem.AddFile(Path.Combine(workerDir, companionFile));
            }
        }
    }

    [Fact]
    public void RequiredCompanionFiles_CoverWorkerRuntimeAssets()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", ".."));
        var binRoot = Path.Combine(repositoryRoot, "TiaMcpServer.OpennessWorker", "bin");

        // NuGet's project.assets.json restore-graph shape (bare TFM vs.
        // RID-qualified targets, or something else entirely) varies across SDK
        // versions and OS, so it isn't a stable source of truth here. The build
        // output directory is: it's the exact set of files CopyOpennessWorker
        // copies into openness-worker/, so compare against that instead.
        var outputDir = Directory.Exists(binRoot)
            ? Directory.EnumerateDirectories(binRoot)
                .Select(configDir => Path.Combine(configDir, "net48"))
                .FirstOrDefault(dir => File.Exists(Path.Combine(dir, "TiaMcpServer.OpennessWorker.exe")))
            : null;

        Assert.False(
            string.IsNullOrEmpty(outputDir),
            $"No built net48 output found under {binRoot}. Build TiaMcpServer.OpennessWorker before running this test.");

        var builtDlls = Directory.EnumerateFiles(outputDir!, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !name!.StartsWith("Siemens.Engineering", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(builtDlls);

        Assert.All(
            builtDlls,
            dependency => Assert.Contains(
                dependency!,
                OpennessWorkerCheck.RequiredCompanionFiles,
                StringComparer.OrdinalIgnoreCase));
        Assert.Contains(
            "TiaMcpServer.OpennessWorker.exe.config",
            OpennessWorkerCheck.RequiredCompanionFiles);
        Assert.DoesNotContain(
            OpennessWorkerCheck.RequiredCompanionFiles,
            file => file.EndsWith("runtimeconfig.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PackageLayout_ExcludesWorkerRuntimeConfigAtCopyAndPackBoundaries()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", ".."));
        var project = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "TiaMcpServer",
            "TiaMcpServer.csproj"));

        Assert.Equal(
            4,
            project.Split("**\\*.runtimeconfig.json", StringSplitOptions.None).Length);
        Assert.Contains("RemoveStaleOpennessWorkerRuntimeConfig", project);
        Assert.DoesNotContain("PackOpennessWorker", project);
    }
}
