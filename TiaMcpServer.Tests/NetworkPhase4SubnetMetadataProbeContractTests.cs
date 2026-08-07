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
            new[] { "read_hardware_config", "probe_network_object_attributes" },
            root.GetProperty("internalWorkerOperations").EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [Fact]
    public void OmittedHardwareConfig_UsesWorkerFallbackAndPreservesOmissionEvidence()
    {
        var escapedScriptPath = ScriptPath.Replace("'", "''", StringComparison.Ordinal);
        var command = $$"""
            $tokens = $null
            $parseErrors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile(
                '{{escapedScriptPath}}',
                [ref] $tokens,
                [ref] $parseErrors)
            if ($parseErrors.Count -ne 0) {
                throw ($parseErrors | ForEach-Object { $_.Message } | Join-String -Separator '; ')
            }

            foreach ($functionName in @('Invoke-HardwareConfig', 'Invoke-WorkerHardwareConfig')) {
                $definition = $ast.Find({
                        param($node)
                        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                            $node.Name -eq $functionName
                    }, $true)
                if ($null -eq $definition) {
                    throw "Function '$functionName' was not found."
                }
                . ([scriptblock]::Create($definition.Extent.Text))
            }

            $ProjectPath = 'C:\fixture\relationship.ap21'
            $script:WorkerProcess = [pscustomobject]@{ Id = 1 }
            $script:WorkerRequestCount = 0
            $script:HardwareConfigSource = $null
            $script:HardwareConfigOmission = $null
            $script:CapturedMessage = $null

            function Invoke-NetworkRead {
                [pscustomobject]@{
                    batch = [pscustomobject]@{
                        operations = @([pscustomobject]@{
                                status = 'omitted'
                                omission = [pscustomobject]@{ reason = 'document_limit' }
                            })
                    }
                }
            }
            function Get-SingleOperationItem {
                param([object] $Envelope, [string] $Description)
                @($Envelope.batch.operations)[0]
            }
            function Connect-Worker {}
            function Send-JsonLine {
                param([object] $Process, [object] $Message)
                $script:CapturedMessage = $Message
            }
            function Read-JsonLine {
                [pscustomobject]@{
                    success = $true
                    payload = '{"devices":[],"subnets":[{"subnetId":"590-2","name":"PN/IE_1"}],"messages":[]}'
                }
            }

            $hardwareConfig = Invoke-HardwareConfig
            [ordered]@{
                source = $script:HardwareConfigSource
                omissionReason = $script:HardwareConfigOmission.reason
                workerRequestCount = $script:WorkerRequestCount
                method = $script:CapturedMessage.method
                projectPath = $script:CapturedMessage.projectPath
                subnetCount = @($hardwareConfig.subnets).Count
                subnetId = $hardwareConfig.subnets[0].subnetId
            } | ConvertTo-Json -Compress -Depth 20
            """;

        var result = RunPowerShellCommand(command);

        Assert.True(
            result.ExitCode == 0,
            $"PowerShell function probe failed.{Environment.NewLine}{result.StandardOutput}{result.StandardError}");
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.Equal("workerFallback", root.GetProperty("source").GetString());
        Assert.Equal("document_limit", root.GetProperty("omissionReason").GetString());
        Assert.Equal(1, root.GetProperty("workerRequestCount").GetInt32());
        Assert.Equal("read_hardware_config", root.GetProperty("method").GetString());
        Assert.Equal(@"C:\fixture\relationship.ap21", root.GetProperty("projectPath").GetString());
        Assert.Equal(1, root.GetProperty("subnetCount").GetInt32());
        Assert.Equal("590-2", root.GetProperty("subnetId").GetString());
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

    private static ScriptResult RunPowerShellCommand(string command)
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
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("PowerShell 7 did not start.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The Phase 4 subnet metadata function probe did not exit within 30 seconds.");
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
