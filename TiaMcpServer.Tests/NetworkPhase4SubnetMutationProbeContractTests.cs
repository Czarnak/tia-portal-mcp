using System.Diagnostics;
using System.Text.Json;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests;

public class NetworkPhase4SubnetMutationProbeContractTests
{
    private const string Acknowledgement = "DELETE-CONNECTED-SUBNETS-IN-DISPOSABLE-PROJECT";

    private static readonly string RepositoryRoot = GetRepositoryRoot();
    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "scripts",
        "live-probe-network-phase4-subnet-mutations.ps1");

    [Fact]
    public void Describe_ReportsTheExplicitMutationContract()
    {
        var result = RunScript("-Describe");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.Equal("network-phase4-subnet-mutation-probe/v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("Inventory", root.GetProperty("defaultMode").GetString());
        Assert.Equal(
            new[] { "Inventory", "Apply" },
            root.GetProperty("modes").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.True(root.GetProperty("applyRequiresAllowMutation").GetBoolean());
        Assert.Equal(Acknowledgement, root.GetProperty("requiredAcknowledgement").GetString());
        Assert.True(root.GetProperty("requiresExplicitConnectedSubnetIds").GetBoolean());
        Assert.Equal(
            new[] { "read_hardware_config", "probe_subnet_lifecycle_mutations" },
            root.GetProperty("internalWorkerOperations").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal("artifacts/live-network-phase4", root.GetProperty("evidenceDirectory").GetString());
    }

    [Fact]
    public void Apply_RejectsMissingAllowMutationBeforeInspectingTheProject()
    {
        var result = RunScript(
            "-Mode", "Apply",
            "-ProjectPath", @"C:\missing\disposable.ap21",
            "-Acknowledgement", Acknowledgement,
            "-ConnectedEthernetSubnetId", "590-2",
            "-ConnectedProfibusSubnetId", "590-3");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("-AllowMutation", result.StandardOutput + result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("project file was not found", result.StandardOutput + result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_RejectsWrongAcknowledgementBeforeInspectingTheProject()
    {
        var result = RunScript(
            "-Mode", "Apply",
            "-ProjectPath", @"C:\missing\disposable.ap21",
            "-AllowMutation",
            "-Acknowledgement", "YES",
            "-ConnectedEthernetSubnetId", "590-2",
            "-ConnectedProfibusSubnetId", "590-3");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(Acknowledgement, result.StandardOutput + result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("project file was not found", result.StandardOutput + result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MutationWorkerRequest_BindsConfirmationAndExactConnectedSubnetIds()
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

            $definition = $ast.Find({
                    param($node)
                    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                        $node.Name -eq 'New-MutationWorkerRequest'
                }, $true)
            if ($null -eq $definition) {
                throw "Function 'New-MutationWorkerRequest' was not found."
            }
            . ([scriptblock]::Create($definition.Extent.Text))

            $request = New-MutationWorkerRequest `
                -ResolvedProjectPath 'C:\fixture\disposable.ap21' `
                -RunId '11132060' `
                -EthernetSubnetId '590-2' `
                -ProfibusSubnetId '590-3' `
                -HighestAddress 125 `
                -TransmissionSpeed 'Baud1500000'
            $request | ConvertTo-Json -Compress -Depth 20
            """;

        var result = RunPowerShellCommand(command);

        Assert.True(
            result.ExitCode == 0,
            $"PowerShell function probe failed.{Environment.NewLine}{result.StandardOutput}{result.StandardError}");
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.Equal("probe_subnet_lifecycle_mutations", root.GetProperty("method").GetString());
        Assert.True(root.GetProperty("confirm").GetBoolean());
        Assert.Equal(@"C:\fixture\disposable.ap21", root.GetProperty("projectPath").GetString());
        Assert.Equal("11132060", root.GetProperty("probeRunId").GetString());
        Assert.Equal("590-2", root.GetProperty("probeConnectedEthernetSubnetId").GetString());
        Assert.Equal("590-3", root.GetProperty("probeConnectedProfibusSubnetId").GetString());
        Assert.Equal(125, root.GetProperty("probeProfibusHighestAddress").GetInt32());
        Assert.Equal("Baud1500000", root.GetProperty("probeProfibusTransmissionSpeed").GetString());
    }

    [Fact]
    public void InternalWorkerOperation_IsDeniedInReadOnlyMode()
    {
        const string operation = "probe_subnet_lifecycle_mutations";

        Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(operation));
        Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, operation));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, operation));
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
            throw new TimeoutException("The Phase 4 subnet mutation probe did not exit within 30 seconds.");
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
            throw new TimeoutException("The Phase 4 subnet mutation function probe did not exit within 30 seconds.");
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
