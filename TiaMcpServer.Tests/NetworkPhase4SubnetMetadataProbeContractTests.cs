using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace TiaMcpServer.Tests;

public class NetworkPhase4SubnetMetadataProbeContractTests
{
    private static readonly string RepositoryRoot = GetRepositoryRoot();
    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "scripts",
        "live-probe-network-phase4-subnet-metadata.ps1");

    [Fact]
    public void Describe_ReportsTheReadOnlySubnetMetadataContract()
    {
        var result = RunScript("-Describe");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.Equal("network-phase4-subnet-metadata-probe/v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("readOnly").GetBoolean());
        Assert.False(root.GetProperty("mutatesProject").GetBoolean());
        Assert.True(root.GetProperty("requiresProjectPath").GetBoolean());
        Assert.Equal(
            new[] { "Ethernet", "Profibus" },
            root.GetProperty("subnetTypes").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(
            new[] { "read_hardware_config", "list_network_objects", "inspect_network_object" },
            root.GetProperty("publicReadOperations").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(
            new[] { "probe_network_object_attributes" },
            root.GetProperty("internalWorkerOperations").EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [Fact]
    public void LiveMode_RejectsMissingProjectPathBeforeStartingAProbe()
    {
        var result = RunScript();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "ProjectPath is required",
            result.StandardOutput + result.StandardError,
            StringComparison.Ordinal);
    }

    private static ScriptResult RunScript(params string[] arguments)
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
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(ScriptPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("PowerShell 7 did not start.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The Phase 4 subnet metadata probe did not exit within 30 seconds.");
        }

        return new ScriptResult(process.ExitCode, standardOutput, standardError);
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private sealed record ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
