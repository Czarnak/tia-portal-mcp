using System.Diagnostics;
using System.IO.Compression;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class DoctorPackageVerificationScriptTests
{
    private static readonly Lazy<string> GlobalPackagesDirectory = new(FindGlobalPackagesDirectory);

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
        startInfo.ArgumentList.Add("Start-Sleep -Seconds 10");

        var stopwatch = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => RunProcess(startInfo, 200, "Slow test process"));
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(8),
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectPack_WithSdk8_IncludesCanonicalWorkerPayload(bool noBuild)
    {
        var workerOutput = FindBuiltWorkerOutput();
        var sdkVersion = FindInstalledSdk8Version();
        var isolatedRoot = Path.Combine(
            Path.GetTempPath(),
            $"openness-worker-sdk8-pack-{Guid.NewGuid():N}");
        var outputPath = Path.Combine(isolatedRoot, "host-output");
        var packageOutput = Path.Combine(isolatedRoot, "packages");
        Directory.CreateDirectory(isolatedRoot);
        File.WriteAllText(
            Path.Combine(isolatedRoot, "global.json"),
            $$"""
            {
              "sdk": {
                "version": "{{sdkVersion}}",
                "rollForward": "disable"
              }
            }
            """);

        try
        {
            var selectedSdk = RunDotnetVersion(isolatedRoot);
            Assert.True(
                selectedSdk.ExitCode == 0,
                $"SDK selection failed.{Environment.NewLine}{selectedSdk.StandardOutput}{Environment.NewLine}{selectedSdk.StandardError}");
            Assert.Equal(sdkVersion, selectedSdk.StandardOutput.Trim());

            var restoreResult = RunWorkerRestore(isolatedRoot);
            Assert.True(
                restoreResult.ExitCode == 0,
                $"SDK 8 worker restore failed.{Environment.NewLine}{restoreResult.StandardOutput}{Environment.NewLine}{restoreResult.StandardError}");

            if (noBuild)
            {
                var buildResult = RunBuild(outputPath, isolatedRoot);
                Assert.True(
                    buildResult.ExitCode == 0,
                    $"SDK 8 prerequisite build failed.{Environment.NewLine}{buildResult.StandardOutput}{Environment.NewLine}{buildResult.StandardError}");
            }

            var copiedWorkerOutput = Path.Combine(outputPath, "openness-worker");
            Directory.CreateDirectory(copiedWorkerOutput);
            File.WriteAllText(Path.Combine(copiedWorkerOutput, "obsolete-worker-assembly.dll"), "stale");

            var packResult = RunPack(outputPath, packageOutput, noBuild, isolatedRoot);
            Assert.True(
                packResult.ExitCode == 0,
                $"SDK 8 direct pack failed (noBuild={noBuild}).{Environment.NewLine}{packResult.StandardOutput}{Environment.NewLine}{packResult.StandardError}");

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

        return RunProcess(startInfo, 30_000, "Package verifier");
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

        return RunProcess(startInfo, 60_000, "Worker copy target");
    }

    private static ScriptResult RunBuild(string outputPath, string? workingDirectory = null)
    {
        var projectPath = Path.Combine(GetRepositoryRoot(), "TiaMcpServer", "TiaMcpServer.csproj");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name;
        Assert.False(string.IsNullOrEmpty(configuration), "Test build configuration could not be determined.");

        var startInfo = CreateDotnetStartInfo(workingDirectory);
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

    private static ScriptResult RunWorkerRestore(string workingDirectory)
    {
        var workerProjectPath = Path.Combine(
            GetRepositoryRoot(),
            "TiaMcpServer.OpennessWorker",
            "TiaMcpServer.OpennessWorker.csproj");
        var startInfo = CreateDotnetStartInfo(workingDirectory);
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(workerProjectPath);
        startInfo.ArgumentList.Add("--disable-build-servers");
        startInfo.ArgumentList.Add("--ignore-failed-sources");
        startInfo.ArgumentList.Add("/p:UseTiaPortalReferenceStubs=true");
        startInfo.ArgumentList.Add("/p:NuGetAudit=false");
        startInfo.ArgumentList.Add("/v:minimal");
        return RunProcess(startInfo, 30_000, "SDK 8 worker restore");
    }

    private static ScriptResult RunPack(
        string outputPath,
        string packageOutput,
        bool noBuild,
        string? workingDirectory = null)
    {
        var projectPath = Path.Combine(GetRepositoryRoot(), "TiaMcpServer", "TiaMcpServer.csproj");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name;
        Assert.False(string.IsNullOrEmpty(configuration), "Test build configuration could not be determined.");

        var startInfo = CreateDotnetStartInfo(workingDirectory);
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

    private static ScriptResult RunDotnetVersion(string workingDirectory)
    {
        var startInfo = CreateDotnetStartInfo(workingDirectory);
        startInfo.ArgumentList.Add("--version");
        return RunProcess(startInfo, 10_000, "SDK version probe");
    }

    private static string FindInstalledSdk8Version()
    {
        var startInfo = CreateDotnetStartInfo();
        startInfo.ArgumentList.Add("--list-sdks");
        var result = RunProcess(startInfo, 10_000, "Installed SDK probe");
        Assert.True(
            result.ExitCode == 0,
            $"Installed SDK probe failed.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");

        var installedSdk8 = result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' ', 2)[0])
            .Select(value => Version.TryParse(value, out var version) ? version : null)
            .Where(version => version?.Major == 8)
            .Max();
        Assert.NotNull(installedSdk8);
        return installedSdk8!.ToString();
    }

    private static string FindGlobalPackagesDirectory()
    {
        var startInfo = CreateDotnetStartInfo();
        startInfo.ArgumentList.Add("nuget");
        startInfo.ArgumentList.Add("locals");
        startInfo.ArgumentList.Add("global-packages");
        startInfo.ArgumentList.Add("--list");
        var result = RunProcess(startInfo, 10_000, "NuGet global-packages probe");
        Assert.True(
            result.ExitCode == 0,
            $"NuGet global-packages probe failed.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");

        var separator = result.StandardOutput.IndexOf(':');
        Assert.True(separator >= 0, $"Unexpected NuGet global-packages output: {result.StandardOutput}");
        var directory = result.StandardOutput[(separator + 1)..].Trim();
        Assert.True(Directory.Exists(directory), $"NuGet global-packages directory does not exist: {directory}");
        return directory;
    }

    private static ProcessStartInfo CreateDotnetStartInfo(string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
            startInfo.Environment["DOTNET_CLI_HOME"] = workingDirectory;
            startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
            startInfo.Environment["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "0";
            startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            startInfo.Environment["NUGET_PACKAGES"] = GlobalPackagesDirectory.Value;
            startInfo.Environment.Remove("MSBuildSDKsPath");
            startInfo.Environment.Remove("MSBUILD_EXE_PATH");
        }

        return startInfo;
    }

    private static ScriptResult RunProcess(ProcessStartInfo startInfo, int timeoutMilliseconds, string operation)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {operation} process.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            if (!process.WaitForExit(5_000))
            {
                throw new TimeoutException($"{operation} did not terminate after its process tree was killed.");
            }

            WaitForRedirectedStreams(standardOutput, standardError, operation);
            throw new TimeoutException($"{operation} did not exit within {timeoutMilliseconds} milliseconds.");
        }

        WaitForRedirectedStreams(standardOutput, standardError, operation);
        return new ScriptResult(
            process.ExitCode,
            standardOutput.GetAwaiter().GetResult(),
            standardError.GetAwaiter().GetResult());
    }

    private static void WaitForRedirectedStreams(Task<string> standardOutput, Task<string> standardError, string operation)
    {
        if (!Task.WaitAll(new Task[] { standardOutput, standardError }, 5_000))
        {
            throw new TimeoutException($"{operation} output streams did not close within 5 seconds.");
        }
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
