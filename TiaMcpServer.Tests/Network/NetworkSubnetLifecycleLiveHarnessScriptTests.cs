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
    public void Harness_LaunchesTheHostFromTheFrozenCheckoutOnly()
    {
        var source = HarnessSource;
        Assert.DoesNotContain("[string] $HostExecutable", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[string[]] $HostArguments", source, StringComparison.Ordinal);
        Assert.Contains("$psi.WorkingDirectory = $script:RepositoryRoot", source, StringComparison.Ordinal);
        Assert.Contains("$psi.FileName = 'dotnet'", source, StringComparison.Ordinal);
        Assert.Contains("$hostProject = Join-Path $script:RepositoryRoot 'TiaMcpServer/TiaMcpServer.csproj'", source, StringComparison.Ordinal);
        Assert.Contains("$psi.ArgumentList.Add($hostProject)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Harness_BindsTheHostToTheExactApprovedProjectBeforeAnyMcpCall()
    {
        var source = HarnessSource;
        var start = source.IndexOf("function Start-McpHost", StringComparison.Ordinal);
        var stop = source.IndexOf("function Stop-McpHost", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && stop > start);
        var launch = source[start..stop];

        Assert.Matches(new Regex(@"\$psi\.ArgumentList\.Add\(\$hostProject\)\s*\[void\]\s*\$psi\.ArgumentList\.Add\('--'\)\s*\[void\]\s*\$psi\.ArgumentList\.Add\('--project'\)\s*\[void\]\s*\$psi\.ArgumentList\.Add\(\$ProjectPath\)"), launch);
        Assert.Contains("[System.IO.Path]::IsPathFullyQualified($ProjectPath)", source[..start], StringComparison.Ordinal);
        Assert.Contains("[System.IO.Path]::GetFullPath($ProjectPath)", source[..start], StringComparison.Ordinal);
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
    public void Harness_InventoryRecordsObservedProjectAndSessionEvidence()
    {
        var source = HarnessSource;
        Assert.Contains("$session = $envelope.sessionIdentity", source, StringComparison.Ordinal);
        Assert.Contains("projectPath     = $project.path", source, StringComparison.Ordinal);
        Assert.Contains("sessionIdentity = $session", source, StringComparison.Ordinal);
        Assert.Contains("portalProcessId = $session.portalProcessId", source, StringComparison.Ordinal);
        Assert.Contains("isModified      = $project.isModified", source, StringComparison.Ordinal);
        Assert.Contains("projectVersion  = $projectVersion", source, StringComparison.Ordinal);
        Assert.Contains("projectStatus   = $projectStatus", source, StringComparison.Ordinal);
        Assert.Contains("$evidence['requestedProjectPath'] = $ProjectPath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("$evidence['projectPath'] = $ProjectPath", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Harness_ProjectEvidenceFailsClosedBeforeRecording()
    {
        var source = HarnessSource;
        Assert.Contains("$project.isOpen -isnot [bool] -or -not $project.isOpen", source, StringComparison.Ordinal);
        Assert.Contains("$project.isModified -isnot [bool]", source, StringComparison.Ordinal);
        Assert.Contains("$session.portalProcessId -le 0", source, StringComparison.Ordinal);
        Assert.Contains("[string]::IsNullOrWhiteSpace($session.workerSessionId)", source, StringComparison.Ordinal);
        Assert.Contains("$session.sessionGeneration -lt 0", source, StringComparison.Ordinal);
        Assert.Contains("foreach ($observedPath in @($project.path, $session.projectPath))", source, StringComparison.Ordinal);
        Assert.Contains("[System.IO.Path]::GetFullPath($observedPath)", source, StringComparison.Ordinal);
        Assert.Contains("[System.StringComparison]::OrdinalIgnoreCase", source, StringComparison.Ordinal);
        Assert.Contains("$project.PSObject.Properties['version']", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Harness_AcceptsUnavailableProjectVersionButRejectsPathMismatch()
    {
        var result = RunStaticAstAssertion("""
            $definition = @($ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                    $node.Name -ceq 'Get-ObservedProjectStatus'
            }, $true))
            if ($definition.Count -ne 1) { throw 'Expected one project status function.' }
            Invoke-Expression $definition[0].Extent.Text

            $ProjectPath = 'C:\fixture.ap21'
            $script:observedProjectPath = $ProjectPath
            $script:includeVersion = $true
            function Invoke-McpToolCall {
                param($Name, $Arguments)
                $projectData = @{
                    path = $script:observedProjectPath
                    isOpen = $true
                    isModified = $false
                }
                if ($script:includeVersion) { $projectData.version = $null }
                $payload = @{ project = $projectData } | ConvertTo-Json -Depth 10
                @{ success = $true; payload = $payload; sessionIdentity = @{
                    projectPath = $ProjectPath
                    portalProcessId = 1234
                    workerSessionId = 'observed-session'
                    sessionGeneration = 1
                } }
            }

            $status = Get-ObservedProjectStatus
            if ($status.projectPath -cne $ProjectPath -or
                $status.sessionIdentity.projectPath -cne $ProjectPath -or
                $status.portalProcessId -ne 1234 -or
                $status.isModified -isnot [bool] -or $status.isModified -or
                $null -ne $status.projectVersion) {
                throw 'Null project version was not recorded with the observed identity.'
            }

            $script:includeVersion = $false
            $status = Get-ObservedProjectStatus
            if ($status.projectPath -cne $ProjectPath -or
                $status.sessionIdentity.projectPath -cne $ProjectPath -or
                $status.isModified -isnot [bool] -or $status.isModified -or
                $null -ne $status.projectVersion) {
                throw 'Omitted project version was not recorded with the observed identity.'
            }

            $script:observedProjectPath = 'C:\other.ap21'
            $rejected = $false
            try { $null = Get-ObservedProjectStatus } catch {
                $rejected = $_.Exception.Message -match 'Observed project/session path does not match'
            }
            if (-not $rejected) { throw 'Mismatched observed project path was accepted.' }

            'nullable-project-version-contract-ok'
            """);

        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("nullable-project-version-contract-ok", result.StandardOutput.Trim());
    }

    [Fact]
    public void Harness_DistinguishesAggregateHardwareCountsFromLifecycleRootCounts()
    {
        var source = HarnessSource;
        Assert.DoesNotMatch(new Regex(@"rootDeviceCount\s*=\s*@\([^\r\n]*devices", RegexOptions.IgnoreCase), source);
        Assert.DoesNotContain("postReadRootDeviceCount", source, StringComparison.Ordinal);
        Assert.Contains("totalHardwareDeviceCount = @($hardware.devices).Count", source, StringComparison.Ordinal);
        Assert.Contains("postReadTotalHardwareDeviceCount = $totalHardwareDeviceCountAfter", source, StringComparison.Ordinal);
        Assert.Contains("$totalHardwareDeviceCountAfter -ne $TotalHardwareDeviceCountBefore", source, StringComparison.Ordinal);
        Assert.Contains("rootDeviceCountEvidenceSource = 'subnet lifecycle results; no independent pre-apply root count'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Harness_HardwareReadAggregatesPagesAndRejectsBrokenContinuations()
    {
        var result = RunStaticAstAssertion("""
            $definition = @($ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                    $node.Name -ceq 'Read-HardwareConfig'
            }, $true))
            if ($definition.Count -ne 1) { throw 'Expected one hardware read function.' }
            Invoke-Expression $definition[0].Extent.Text
            $ProjectPath = 'C:\fixture.ap21'
            $script:requests = @()
            $script:pages = @()
            function Invoke-McpToolCall {
                param($Name, $Arguments)
                $script:requests += $Arguments.operations[0]
                $page = $script:pages[$script:requests.Count - 1]
                return (@{ batch = @{ operations = @($page) } } |
                    ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20)
            }
            function Page($devices, $subnets, $returnedDevices, $returnedSubnets, $cursor) {
                @{ status = 'succeeded'; result = @{
                    devices = @($devices); subnets = @($subnets); messages = @(); pagination = @{
                        totalDevices = 2; totalSubnets = 1
                        returnedDevices = $returnedDevices; returnedSubnets = $returnedSubnets
                        nextCursor = $cursor
                    }
                } }
            }
            $script:pages = @(
                (Page @(@{ name = 'a' }) @() 1 0 'cursor-1'),
                (Page @(@{ name = 'b' }) @(@{ subnetId = 's' }) 1 1 $null)
            )
            $script:pages[1].result.pagination.Remove('nextCursor')
            $hardware = Read-HardwareConfig
            if (@($hardware.devices).Count -ne 2 -or @($hardware.subnets).Count -ne 1 -or
                $script:requests.Count -ne 2 -or $script:requests[0].pageSize -lt 1 -or
                $script:requests[0].pageSize -gt 200 -or
                $script:requests[1].cursor -cne 'cursor-1' -or
                $script:requests[1].projectPath -cne $ProjectPath) {
                throw 'Paged read did not aggregate all entities using the opaque cursor.'
            }

            foreach ($badSecondPage in @(
                (Page @() @() 0 0 'cursor-2'),
                (Page @(@{ name = 'b' }) @() 1 0 'cursor-1'),
                (Page @(@{ name = 'b' }) @() 2 0 'cursor-2'),
                @{ status = 'succeeded'; result = @{
                    devices = @(@{ name = 'b' }); subnets = @(@{ subnetId = 's' }); messages = @()
                    pagination = @{ totalDevices = 3; totalSubnets = 1; returnedDevices = 1; returnedSubnets = 1 }
                } },
                (Page @() @() 0 0 $null),
                @{ status = 'omitted'; omission = @{ reason = 'oversized' } },
                @{ status = 'failed'; failure = @{ category = 'read_error' } },
                @{ status = 'succeeded'; result = @{ devices = @(); subnets = @() } }
            )) {
                $script:requests = @()
                $script:pages = @((Page @(@{ name = 'a' }) @() 1 0 'cursor-1'), $badSecondPage)
                $rejected = $false
                try { $null = Read-HardwareConfig } catch { $rejected = $true }
                if (-not $rejected) { throw 'Invalid continuation was accepted.' }
            }
            'paged-hardware-contract-ok'
            """);

        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("paged-hardware-contract-ok", result.StandardOutput.Trim());
    }

    [Fact]
    public void Harness_RequiresConsistentRootCountsFromEveryLifecycleGroup()
    {
        var source = HarnessSource;
        Assert.Contains("$item.result.networkDeviceCountUnchanged -isnot [bool]", source, StringComparison.Ordinal);
        Assert.Contains("$item.result.networkDeviceCount -lt 0", source, StringComparison.Ordinal);
        Assert.Contains("$RootDeviceCount.Value = $item.result.networkDeviceCount", source, StringComparison.Ordinal);
        Assert.Contains("$item.result.networkDeviceCount -ne $RootDeviceCount.Value", source, StringComparison.Ordinal);
        Assert.Contains("networkDeviceCountUnchanged = $item.result.networkDeviceCountUnchanged", source, StringComparison.Ordinal);
        Assert.Equal(4, Regex.Matches(source, @"-RootDeviceCount \(\[ref\]\$rootDeviceCount\)").Count);
        Assert.Contains("finalRootDeviceCount     = $rootDeviceCount", source, StringComparison.Ordinal);
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
