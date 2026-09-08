using System.Diagnostics;
using System.IO.Compression;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class DoctorPackageVerificationScriptTests
{
    [Fact]
    public void ProcessRunner_EnforcesTimeoutBeforeSlowChildNaturallyExits()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("Start-Sleep -Seconds 2");

        var stopwatch = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => RunProcess(startInfo, 200, "Slow test process"));
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(1_500),
            $"Process timeout took {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectPack_RefreshesSeededWorkerFilesBeforeCollectingPackageItems(bool noBuild)
    {
        var workerOutput = FindBuiltWorkerOutput();
        var isolatedRoot = Path.Combine(
            Path.GetTempPath(),
            $"openness-worker-pack-{Guid.NewGuid():N}");
        var outputPath = Path.Combine(isolatedRoot, "host-output");
        var packageOutput = Path.Combine(isolatedRoot, "packages");

        try
        {
            if (noBuild)
            {
                var buildResult = RunBuild(outputPath);
                Assert.True(
                    buildResult.ExitCode == 0,
                    $"Isolated prerequisite build failed.{Environment.NewLine}{buildResult.StandardOutput}{Environment.NewLine}{buildResult.StandardError}");
            }

            var copiedWorkerOutput = Path.Combine(outputPath, "openness-worker");
            Directory.CreateDirectory(copiedWorkerOutput);
            File.WriteAllText(Path.Combine(copiedWorkerOutput, "obsolete-worker-assembly.dll"), "stale");

            var packResult = RunPack(outputPath, packageOutput, noBuild);
            Assert.True(
                packResult.ExitCode == 0,
                $"Direct pack failed (noBuild={noBuild}).{Environment.NewLine}{packResult.StandardOutput}{Environment.NewLine}{packResult.StandardError}");

            var packagePath = Path.Combine(packageOutput, "TiaMcpServer.3.0.0.nupkg");
            Assert.True(File.Exists(packagePath), $"Expected package was not created at {packagePath}.");
            Assert.Equal(
                EnumerateAuthoritativeWorkerFiles(workerOutput),
                EnumeratePackagedWorkerFiles(packagePath));
        }
        finally
        {
            Directory.Delete(isolatedRoot, recursive: true);
        }
    }

    [Fact]
    public void CopyTarget_MirrorsAuthoritativeWorkerOutputWithoutObsoleteFiles()
    {
        var workerOutput = FindBuiltWorkerOutput();
        var isolatedOutput = Path.Combine(
            Path.GetTempPath(),
            $"openness-worker-copy-{Guid.NewGuid():N}");
        var copiedWorkerOutput = Path.Combine(isolatedOutput, "openness-worker");
        Directory.CreateDirectory(copiedWorkerOutput);
        File.WriteAllText(Path.Combine(copiedWorkerOutput, "obsolete-worker-assembly.dll"), "stale");
        File.WriteAllText(Path.Combine(copiedWorkerOutput, "TiaMcpServer.OpennessWorker.runtimeconfig.json"), "stale");
        File.WriteAllText(Path.Combine(copiedWorkerOutput, "Siemens.Engineering.dll"), "stale");

        try
        {
            var result = RunCopyTarget(isolatedOutput);

            Assert.True(
                result.ExitCode == 0,
                $"Worker copy target failed.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");

            var expectedFiles = EnumerateAuthoritativeWorkerFiles(workerOutput);
            var actualFiles = EnumerateRelativeFiles(copiedWorkerOutput);
            Assert.Equal(expectedFiles, actualFiles);
        }
        finally
        {
            Directory.Delete(isolatedOutput, recursive: true);
        }
    }

    [Fact]
    public void BuiltWorkerPayload_PassesPackageVerification()
    {
        var workerOutput = FindBuiltWorkerOutput();
        var packagePath = CreatePackage(workerOutput);

        try
        {
            var result = RunVerifier(packagePath);

            Assert.True(
                result.ExitCode == 0,
                $"Package verifier failed.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Fact]
    public void WorkerPayloadWithoutValueTuple_FailsPackageVerification()
    {
        var workerOutput = FindBuiltWorkerOutput();
        var packagePath = CreatePackage(workerOutput, excludedFile: "System.ValueTuple.dll");

        try
        {
            var result = RunVerifier(packagePath);
            var output = result.StandardOutput + Environment.NewLine + result.StandardError;

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("System.ValueTuple.dll", output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Fact]
    public void WorkerPayloadWithUnexpectedPipelineAssembly_FailsPackageVerification()
    {
        var workerOutput = FindBuiltWorkerOutput();
        var packagePath = CreatePackage(
            workerOutput,
            additionalEntry: "System.IO.Pipelines.dll");

        try
        {
            var result = RunVerifier(packagePath);
            var output = result.StandardOutput + Environment.NewLine + result.StandardError;

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("System.IO.Pipelines.dll", output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    private static string CreatePackage(
        string workerOutput,
        string? excludedFile = null,
        string? additionalEntry = null)
    {
        var packagePath = Path.Combine(
            Path.GetTempPath(),
            $"doctor-package-{Guid.NewGuid():N}.nupkg");

        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        foreach (var builtFile in Directory.EnumerateFiles(workerOutput, "*", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(Path.GetFileName(builtFile), excludedFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var entryName = "tools/net8.0/any/openness-worker/" + Path.GetFileName(builtFile);
            archive.CreateEntryFromFile(builtFile, entryName);
        }

        if (additionalEntry is not null)
        {
            archive.CreateEntry("tools/net8.0/any/openness-worker/" + additionalEntry);
        }

        return packagePath;
    }

    private static string FindBuiltWorkerOutput()
    {
        var repositoryRoot = GetRepositoryRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name;
        Assert.False(string.IsNullOrEmpty(configuration), "Test build configuration could not be determined.");

        var outputDir = Path.Combine(
            repositoryRoot,
            "TiaMcpServer.OpennessWorker",
            "bin",
            configuration!,
            "net48");
        Assert.True(
            File.Exists(Path.Combine(outputDir, "TiaMcpServer.OpennessWorker.exe")),
            $"No built net48 output found under {outputDir}. Build TiaMcpServer.OpennessWorker before running this test.");
        return outputDir;
    }

    private static ScriptResult RunVerifier(string packagePath)
    {
        var scriptPath = Path.Combine(GetRepositoryRoot(), "scripts", "verify-doctor-package.ps1");
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-PackagePath");
        startInfo.ArgumentList.Add(packagePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start pwsh process.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Package verifier did not exit within 30 seconds.");
        }

        return new ScriptResult(process.ExitCode, standardOutput, standardError);
    }

    private static ScriptResult RunCopyTarget(string outputPath)
    {
        var projectPath = Path.Combine(GetRepositoryRoot(), "TiaMcpServer", "TiaMcpServer.csproj");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name;
        Assert.False(string.IsNullOrEmpty(configuration), "Test build configuration could not be determined.");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("/t:CopyOpennessWorker");
        startInfo.ArgumentList.Add("/m:1");
        startInfo.ArgumentList.Add("/nr:false");
        startInfo.ArgumentList.Add($"/p:Configuration={configuration}");
        startInfo.ArgumentList.Add($"/p:OutputPath={Path.TrimEndingDirectorySeparator(outputPath)}{Path.DirectorySeparatorChar}");
        startInfo.ArgumentList.Add("/p:UseTiaPortalReferenceStubs=true");
        startInfo.ArgumentList.Add("/v:minimal");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet msbuild process.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Worker copy target did not exit within 60 seconds.");
        }

        return new ScriptResult(process.ExitCode, standardOutput, standardError);
    }

    private static ScriptResult RunBuild(string outputPath)
    {
        var projectPath = Path.Combine(GetRepositoryRoot(), "TiaMcpServer", "TiaMcpServer.csproj");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name;
        Assert.False(string.IsNullOrEmpty(configuration), "Test build configuration could not be determined.");

        var startInfo = CreateDotnetStartInfo();
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("--disable-build-servers");
        startInfo.ArgumentList.Add("-m:1");
        startInfo.ArgumentList.Add($"/p:Configuration={configuration}");
        startInfo.ArgumentList.Add($"/p:OutputPath={Path.TrimEndingDirectorySeparator(outputPath)}{Path.DirectorySeparatorChar}");
        startInfo.ArgumentList.Add("/p:UseTiaPortalReferenceStubs=true");
        startInfo.ArgumentList.Add("/v:minimal");
        return RunProcess(startInfo, 60_000, "Isolated build");
    }

    private static ScriptResult RunPack(string outputPath, string packageOutput, bool noBuild)
    {
        var projectPath = Path.Combine(GetRepositoryRoot(), "TiaMcpServer", "TiaMcpServer.csproj");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name;
        Assert.False(string.IsNullOrEmpty(configuration), "Test build configuration could not be determined.");

        var startInfo = CreateDotnetStartInfo();
        startInfo.ArgumentList.Add("pack");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("--disable-build-servers");
        if (noBuild)
        {
            startInfo.ArgumentList.Add("--no-build");
        }

        startInfo.ArgumentList.Add("-m:1");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(packageOutput);
        startInfo.ArgumentList.Add($"/p:Configuration={configuration}");
        startInfo.ArgumentList.Add($"/p:OutputPath={Path.TrimEndingDirectorySeparator(outputPath)}{Path.DirectorySeparatorChar}");
        startInfo.ArgumentList.Add("/p:Version=3.0.0");
        startInfo.ArgumentList.Add("/p:PackageVersion=3.0.0");
        startInfo.ArgumentList.Add("/p:InformationalVersion=3.0.0");
        startInfo.ArgumentList.Add("/p:IncludeSourceRevisionInInformationalVersion=false");
        startInfo.ArgumentList.Add("/p:UseTiaPortalReferenceStubs=true");
        startInfo.ArgumentList.Add("/v:minimal");
        return RunProcess(startInfo, 90_000, "Direct pack");
    }

    private static ProcessStartInfo CreateDotnetStartInfo()
    {
        return new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }

    private static ScriptResult RunProcess(ProcessStartInfo startInfo, int timeoutMilliseconds, string operation)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {operation} process.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{operation} did not exit within {timeoutMilliseconds} milliseconds.");
        }

        return new ScriptResult(process.ExitCode, standardOutput, standardError);
    }

    private static string[] EnumerateAuthoritativeWorkerFiles(string workerOutput)
    {
        return Directory.EnumerateFiles(workerOutput, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("Siemens.Engineering", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(workerOutput, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] EnumerateRelativeFiles(string directory)
    {
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] EnumeratePackagedWorkerFiles(string packagePath)
    {
        const string prefix = "tools/net8.0/any/openness-worker/";
        using var archive = ZipFile.OpenRead(packagePath);
        return archive.Entries
            .Where(entry => entry.FullName.StartsWith(prefix, StringComparison.Ordinal))
            .Select(entry => entry.FullName[prefix.Length..])
            .Where(path => path.Length > 0)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."));
    }

    private sealed record ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
