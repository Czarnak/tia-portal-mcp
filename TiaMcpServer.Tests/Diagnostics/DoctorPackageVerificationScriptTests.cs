using System.Diagnostics;
using System.IO.Compression;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class DoctorPackageVerificationScriptTests
{
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
