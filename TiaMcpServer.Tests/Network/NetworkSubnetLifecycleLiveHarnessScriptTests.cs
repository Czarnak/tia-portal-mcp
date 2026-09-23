using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public sealed class NetworkSubnetLifecycleLiveHarnessScriptTests
{
    private static string HarnessSource => File.ReadAllText(FindRepositoryFile(
        "scripts", "live-test-network-phase4-subnets.ps1"));

    [Fact]
    public void Harness_DefaultsToInventoryAndUsesStrictPowerShell7()
    {
        var source = HarnessSource;
        Assert.Contains("#Requires -Version 7", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Set-StrictMode -Version Latest", source, StringComparison.Ordinal);
        Assert.Contains("$ErrorActionPreference = 'Stop'", source, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"\[ValidateSet\('Inventory',\s*'Preview',\s*'Apply'\)\][\s\S]*?\$Mode\s*=\s*'Inventory'"),
            source);
    }

    [Fact]
    public void Harness_ApplyIsDoubleGatedBeforeTheHostStarts()
    {
        var source = HarnessSource;
        var applyGate = source.IndexOf("if ($Mode -eq 'Apply')", StringComparison.Ordinal);
        var hostDefinition = source.IndexOf("function Start-McpHost", StringComparison.Ordinal);

        Assert.True(applyGate >= 0 && hostDefinition > applyGate);
        Assert.Contains("if (-not $AllowMutation)", source, StringComparison.Ordinal);
        Assert.Contains("$Acknowledgement -cne $script:RequiredAcknowledgement", source, StringComparison.Ordinal);
        Assert.Contains("DELETE SUBNETS AND KEEP DEVICES", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Harness_ConfirmedNetworkWriteIsReachableOnlyThroughDoubleGatedApply()
    {
        var result = RunStaticAstAssertion("""
            function Get-OwningFunction([System.Management.Automation.Language.Ast] $Node) {
                for ($cursor = $Node.Parent; $null -ne $cursor; $cursor = $cursor.Parent) {
                    if ($cursor -is [System.Management.Automation.Language.FunctionDefinitionAst]) {
                        return $cursor
                    }
                }
                return $null
            }

            function Get-Calls([string] $Name) {
                @($ast.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.CommandAst] -and
                        $node.GetCommandName() -ceq $Name
                }, $true))
            }

            function Assert-OnlyCalledFrom([string] $Callee, [string] $Caller) {
                $calls = @(Get-Calls $Callee)
                if ($calls.Count -eq 0) { throw "Expected at least one call to $Callee." }
                foreach ($call in $calls) {
                    $owner = Get-OwningFunction $call
                    if ($null -eq $owner -or $owner.Name -cne $Caller) {
                        throw "$Callee is callable outside $Caller."
                    }
                }
            }

            $networkWriteCalls = @($ast.FindAll({
                param($node)
                if ($node -isnot [System.Management.Automation.Language.CommandAst]) {
                    return $false
                }
                if ($node.GetCommandName() -notin @(
                    'Invoke-McpToolCall',
                    'Invoke-McpToolCallExpectingError')) {
                    return $false
                }
                $node.Extent.Text -match '(?i)-Name\s+[''"]network_write[''"]'
            }, $true))
            if ($networkWriteCalls.Count -eq 0) { throw 'No public network_write calls found.' }

            $confirmedCalls = @()
            foreach ($call in $networkWriteCalls) {
                $confirm = [regex]::Matches(
                    $call.Extent.Text,
                    '(?i)\bconfirm\s*=\s*\$(true|false)\b')
                if ($confirm.Count -ne 1) {
                    throw 'Every network_write call must use one literal confirm boolean.'
                }
                if ($confirm[0].Groups[1].Value -ieq 'true') { $confirmedCalls += $call }
            }
            if ($confirmedCalls.Count -ne 1) {
                throw "Expected exactly one confirmed network_write; found $($confirmedCalls.Count)."
            }
            $confirmedOwner = Get-OwningFunction $confirmedCalls[0]
            if ($null -eq $confirmedOwner -or $confirmedOwner.Name -cne 'Invoke-NetworkWriteApply') {
                throw 'The confirmed network_write is outside Invoke-NetworkWriteApply.'
            }

            Assert-OnlyCalledFrom 'Invoke-NetworkWriteApply' 'Invoke-LifecycleGroupAndVerify'
            Assert-OnlyCalledFrom 'Invoke-LifecycleGroupAndVerify' 'Invoke-Apply'

            $applyCalls = @(Get-Calls 'Invoke-Apply')
            if ($applyCalls.Count -ne 1 -or $null -ne (Get-OwningFunction $applyCalls[0])) {
                throw 'Invoke-Apply must have exactly one top-level call site.'
            }
            $switch = $applyCalls[0].Parent
            while ($null -ne $switch -and
                   $switch -isnot [System.Management.Automation.Language.SwitchStatementAst]) {
                $switch = $switch.Parent
            }
            if ($null -eq $switch -or $switch.Condition.Extent.Text.Trim() -cne '$Mode') {
                throw 'Invoke-Apply is not dispatched by switch ($Mode).'
            }
            $owningClauses = @($switch.Clauses | Where-Object {
                $applyCalls[0].Extent.StartOffset -ge $_.Item2.Extent.StartOffset -and
                $applyCalls[0].Extent.EndOffset -le $_.Item2.Extent.EndOffset
            })
            if ($owningClauses.Count -ne 1 -or
                $owningClauses[0].Item1.Extent.Text -cne "'Apply'") {
                throw 'Invoke-Apply is reachable from a non-Apply switch clause.'
            }

            $applyGates = @($ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.IfStatementAst] -and
                    $node.Clauses.Count -gt 0 -and
                    $node.Clauses[0].Item1.Extent.Text -match
                        '^\s*\$Mode\s+-eq\s+[''"]Apply[''"]\s*$'
            }, $true) | Where-Object { $null -eq (Get-OwningFunction $_) })
            if ($applyGates.Count -ne 1) { throw 'Expected one top-level Apply gate.' }
            $gateText = $applyGates[0].Clauses[0].Item2.Extent.Text
            if ($gateText -notmatch 'if\s*\(\s*-not\s+\$AllowMutation\s*\)' -or
                $gateText -notmatch '\$Acknowledgement\s+-cne\s+\$script:RequiredAcknowledgement') {
                throw 'Apply gate does not enforce both mutation acknowledgements.'
            }

            $hostStarts = @(Get-Calls 'Connect-McpHost' |
                Where-Object { $null -eq (Get-OwningFunction $_) })
            if ($hostStarts.Count -ne 1 -or
                $applyGates[0].Extent.EndOffset -ge $hostStarts[0].Extent.StartOffset) {
                throw 'Apply gates do not dominate the top-level host start.'
            }

            $candidateChecks = @(Get-Calls 'Assert-FrozenCandidate' |
                Where-Object { $null -eq (Get-OwningFunction $_) })
            if ($candidateChecks.Count -ne 1 -or
                $candidateChecks[0].Extent.EndOffset -ge $hostStarts[0].Extent.StartOffset) {
                throw 'Frozen-candidate validation does not dominate the top-level host start.'
            }

            'apply-path-static-contract-ok'
            """);

        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("apply-path-static-contract-ok", result.StandardOutput.Trim());
    }

    [Fact]
    public void Harness_FrozenCandidateGuardBindsCommitTreeHarnessAndCleanPaths()
    {
        var source = HarnessSource;
        Assert.Contains("rev-parse HEAD", source, StringComparison.Ordinal);
        Assert.Contains("HEAD^{tree}", source, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash -LiteralPath $PSCommandPath", source, StringComparison.Ordinal);
        Assert.Contains("$ExpectedCommit", source, StringComparison.Ordinal);
        Assert.Contains("$ExpectedTree", source, StringComparison.Ordinal);
        Assert.Contains("$ExpectedHarnessSha256", source, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"\$testedCommit\s+-cne\s+\$ExpectedCommit"), source);
        Assert.Matches(new Regex(@"\$testedTree\s+-cne\s+\$ExpectedTree"), source);
        Assert.Matches(
            new Regex(@"\$testedHarnessSha256\s+-ine\s+\$ExpectedHarnessSha256"),
            source);
        Assert.Contains("status --porcelain=v1", source, StringComparison.Ordinal);
        foreach (var path in new[]
                 {
                     "TiaMcpServer", "TiaMcpServer.Contracts", "TiaMcpServer.OpennessWorker",
                     "TiaMcpServer.FakeWorker", "TiaMcpServer.Tests",
                     "scripts/live-test-network-phase4-subnets.ps1",
                 })
        {
            Assert.Contains(path, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Harness_UsesOnlyThePublicMcpRoute()
    {
        var source = HarnessSource;
        foreach (var method in new[]
                 {
                     "initialize", "notifications/initialized", "tools/list",
                     "get_project_status", "network_read", "network_write",
                 })
        {
            Assert.Contains(method, source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("probe_subnet_lifecycle_mutations", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"name\s*=\s*['""]save_project['""]"), source);
        Assert.DoesNotMatch(new Regex(@"name\s*=\s*['""]compile_check['""]"), source);
    }

    [Fact]
    public void Harness_RequiresAnAp21PathAndExactConnectedSubnetIds()
    {
        var source = HarnessSource;
        Assert.Contains(".ap21", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConnectedEthernetSubnetId", source, StringComparison.Ordinal);
        Assert.Contains("ConnectedProfibusSubnetId", source, StringComparison.Ordinal);
        Assert.Contains("subnetId", source, StringComparison.Ordinal);
        Assert.Contains("A name is never accepted", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Harness_RedactsTokensWritesOneTimestampedArtifactAndAlwaysStopsTheHost()
    {
        var source = HarnessSource;
        Assert.Contains("artifacts/live-network-phase4", source, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", source, StringComparison.Ordinal);
        Assert.Contains("finally", source, StringComparison.Ordinal);
        Assert.Contains("Stop-McpHost", source, StringComparison.Ordinal);
        Assert.Contains("rev-parse HEAD", source, StringComparison.Ordinal);
        Assert.Contains("HEAD^{tree}", source, StringComparison.Ordinal);
        Assert.Contains("testedCommit", source, StringComparison.Ordinal);
        Assert.Contains("testedTree", source, StringComparison.Ordinal);
        Assert.Contains("ExpectedCommit", source, StringComparison.Ordinal);
        Assert.Contains("ExpectedTree", source, StringComparison.Ordinal);
        Assert.Contains("ExpectedHarnessSha256", source, StringComparison.Ordinal);
        Assert.Contains("Assert-FrozenCandidate", source, StringComparison.Ordinal);
        Assert.Contains("TIA", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Harness_RecordsTheUnchangedRootDeviceCountContract()
    {
        var source = HarnessSource;
        Assert.Contains("networkDeviceCount", source, StringComparison.Ordinal);
        Assert.Contains("networkDeviceCountUnchanged", source, StringComparison.Ordinal);
        Assert.Contains("ConnectedNodeNames", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"device[^\r\n]*\.Delete\("), source);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.sln")))
            {
                return Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            }
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static ScriptResult RunStaticAstAssertion(string assertionBody)
    {
        var harnessPath = FindRepositoryFile("scripts", "live-test-network-phase4-subnets.ps1");
        var syntheticSource = $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $tokens = $null
            $parseErrors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile(
                {{PowerShellLiteral(harnessPath)}},
                [ref] $tokens,
                [ref] $parseErrors)
            if ($parseErrors.Count -ne 0) {
                throw ($parseErrors | ForEach-Object Message | Out-String)
            }

            {{assertionBody}}
            """;
        var syntheticPath = Path.Combine(
            Path.GetTempPath(),
            $"phase4-harness-static-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(
            syntheticPath,
            syntheticSource,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        try
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
            startInfo.ArgumentList.Add(syntheticPath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start pwsh.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
                throw new TimeoutException("Static harness parser timed out.");
            }

            return new ScriptResult(
                process.ExitCode,
                standardOutput.GetAwaiter().GetResult(),
                standardError.GetAwaiter().GetResult());
        }
        finally
        {
            File.Delete(syntheticPath);
        }
    }

    private static string PowerShellLiteral(string value)
        => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private sealed record ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
