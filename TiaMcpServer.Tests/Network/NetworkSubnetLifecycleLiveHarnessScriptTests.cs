using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public sealed class NetworkSubnetLifecycleLiveHarnessScriptTests
{
    private static string EntryPointSource => File.ReadAllText(FindRepositoryFile(
        "scripts", "live-test-network-phase4-subnets.ps1"));
    private static string SharedSource => File.ReadAllText(FindRepositoryFile("scripts", "network-live-mcp-helpers.ps1"));
    private static string HarnessSource => EntryPointSource + "\n" + SharedSource;

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
    public void Harness_ActualNetworkWriteIsReachableOnlyThroughDoubleGatedApply()
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

            $actualCalls = @()
            foreach ($call in $networkWriteCalls) {
                $dryRun = [regex]::Matches(
                    $call.Extent.Text,
                    '(?i)\bdryRun\s*=\s*\$(true|false)\b')
                if ($dryRun.Count -ne 1) {
                    throw 'Every network_write call must use one literal dryRun boolean.'
                }
                if ($dryRun[0].Groups[1].Value -ieq 'false') { $actualCalls += $call }
            }
            if ($actualCalls.Count -ne 1) {
                throw "Expected exactly one actual network_write; found $($actualCalls.Count)."
            }
            $actualOwner = Get-OwningFunction $actualCalls[0]
            if ($null -eq $actualOwner -or $actualOwner.Name -cne 'Invoke-NetworkWriteApply') {
                throw 'The actual network_write is outside Invoke-NetworkWriteApply.'
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
        Assert.Contains("Get-FileHash -LiteralPath $HarnessPath", source, StringComparison.Ordinal);
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
                     "scripts/live-test-network-phase4-subnets.ps1", "scripts/network-live-mcp-helpers.ps1",
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
    public void Harness_WritesOneTimestampedArtifactAndAlwaysStopsTheHost()
    {
        var source = HarnessSource;
        Assert.Contains("artifacts/live-network-phase4", source, StringComparison.Ordinal);
        Assert.DoesNotContain("safetyToken", source, StringComparison.Ordinal);
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
        Assert.Contains("$project = $envelope.result.value", source, StringComparison.Ordinal);
        Assert.Contains("projectPath     = $project.path", source, StringComparison.Ordinal);
        Assert.Contains("binding         = $script:ObservedBinding", source, StringComparison.Ordinal);
        Assert.Contains("bind_project", source, StringComparison.Ordinal);
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
        Assert.Contains("$bound.result.value.binding.portalProcessId -le 0", source, StringComparison.Ordinal);
        Assert.Contains("foreach ($observedPath in @($project.path))", source, StringComparison.Ordinal);
        Assert.Contains("[System.IO.Path]::GetFullPath($observedPath)", source, StringComparison.Ordinal);
        Assert.Contains("[System.StringComparison]::OrdinalIgnoreCase", source, StringComparison.Ordinal);
        Assert.Contains("$project.PSObject.Properties['version']", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Harness_AcceptsUnavailableProjectVersionButRejectsPathMismatch()
    {
        var result = RunStaticAstAssertion("""
            $ast = $helperAst
            $definition = @($ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                    $node.Name -ceq 'Get-ObservedProjectStatus'
            }, $true))
            if ($definition.Count -ne 1) { throw 'Expected one project status function.' }
            Invoke-Expression $definition[0].Extent.Text

            $ProjectPath = 'C:\fixture.ap21'
            $script:observedProjectPath = $ProjectPath
            $script:ObservedBinding = $null
            $script:includeVersion = $true
            function Invoke-McpToolCall {
                param($Name, $Arguments)
                $projectData = @{
                    path = $script:observedProjectPath
                    isOpen = $true
                    isModified = $false
                }
                if ($script:includeVersion) { $projectData.version = $null }
                @{ success = $true; error = $null; result = @{ status = "succeeded"; value = $projectData } }
            }

            $status = Get-ObservedProjectStatus
            if ($status.projectPath -cne $ProjectPath -or
                $status.isModified -isnot [bool] -or $status.isModified -or
                $null -ne $status.projectVersion) {
                throw 'Null project version was not recorded with the observed identity.'
            }

            $script:includeVersion = $false
            $status = Get-ObservedProjectStatus
            if ($status.projectPath -cne $ProjectPath -or
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
            $ast = $helperAst
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
            $hardware = Read-HardwareConfig -Paged
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
                try { $null = Read-HardwareConfig -Paged } catch { $rejected = $true }
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

    [Theory]
    [InlineData("empty-verification")]
    [InlineData("null-immediate")]
    [InlineData("missing-batch")]
    [InlineData("wrong-operation-id")]
    [InlineData("wrong-operation")]
    [InlineData("wrong-subnet")]
    [InlineData("duplicate-final")]
    [InlineData("missing-final")]
    [InlineData("contradictory-final")]
    [InlineData("wrong-attribute")]
    [InlineData("wrong-post-name")]
    [InlineData("duplicate-batch")]
    [InlineData("duplicate-immediate")]
    [InlineData("null-checks")]
    [InlineData("missing-immediate")]
    [InlineData("wrong-immediate-id")]
    [InlineData("wrong-immediate-status")]
    [InlineData("omitted-verification")]
    [InlineData("wrong-final-id")]
    [InlineData("wrong-post-address")]
    [InlineData("wrong-post-speed")]
    [InlineData("null-post-attribute")]
    [InlineData("duplicate-post-attribute")]
    [InlineData("missing-affected-final")]
    [InlineData("wrong-affected-post")]
    [InlineData("reordered-batch")]
    [InlineData("valid")]
    [InlineData("valid-create")]
    [InlineData("valid-delete")]
    [InlineData("valid-repeat")]
    [InlineData("valid-update-delete")]
    public void LifecycleGroup_RequiresCompleteTypedEvidenceAndRetainsCanonicalApply(string variant)
    {
        var result = RunStaticAstAssertion("""
            # Load definitions only; never invoke a harness mode or real transport.
            foreach ($definition in $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) {
                Invoke-Expression $definition.Extent.Text
            }
            function Check($name, $value) { @{ name=$name; expected=$value; observed=$value; status='passed'; message=$null } }
            $ProjectPath='C:/fixture.ap21'
            $operations = @(@{ operationId='update'; operation='update_subnet'; target=@{ kind='subnet'; subnetId='s' }; subnetChanges=@{ name='After'; highestAddress=63; transmissionSpeed='Baud1500000' } })
            $checks = @((Check 'subnetIdentity' 's'), (Check 'networkDeviceCountUnchanged' '2'), (Check 'Name' 'After'), (Check 'HighestAddress' '63'), (Check 'TransmissionSpeed' 'Baud1500000'))
            $immediate = @{ status='passed'; identity=@{ subnetId='s' }; checks=$checks }
            $script:applied = @{
                contractVersion='1.0'; phase='applied'; success=$true; error=$null; omission=$null
                effects=@(@{ operationId='update'; effect=@{ operation='update_subnet'; target=@{subnetId='s'}; affectedNodes=@(); rootDeviceCount=2; connectionsComplete=$true }; omission=$null })
                batch=@{ operations=@(@{ operationId='update'; operation='update_subnet'; status='succeeded'; failure=$null; omission=$null; result=@{ subnetId='s'; name='After'; networkDeviceCount=2; networkDeviceCountUnchanged=$true; verification=$immediate } }) }
                verification=@{ success=$true; omission=$null; operations=@(@{ operationId='update'; operation='update_subnet'; status='passed'; evidence=$immediate; omission=$null }); finalChecks=@((Check 'networkDeviceCountUnchanged' '2'), (Check 'subnet/s///exists' 'true'), (Check 'subnet/s///Name' 'After'), (Check 'subnet/s///HighestAddress' '63'), (Check 'subnet/s///TransmissionSpeed' 'Baud1500000')) }
            }
            $script:preview = @{ success=$true; effects=$script:applied.effects }
            $script:post = @{ discoveryEvidence=@{scope='project';complete=$true;failures=@()}; devices=@(@{name='a'},@{name='b'}); subnets=@(@{subnetId='s';name='After';typeIdentifier='System:Subnet.Profibus'}) }
            $script:postNodes=@()
            $script:attributeAddress=63; $script:attributeSpeed='Baud1500000'
            $script:nullAttribute=$false; $script:duplicateAttribute=$false
            if ('__VARIANT__' -eq 'valid-create') {
                $operations=@(@{operationId='update';operation='create_subnet';subnet=@{name='After';networkType='Profibus';highestAddress=63;transmissionSpeed='Baud1500000'}})
                $script:applied.batch.operations[0].operation='create_subnet'
                $script:applied.verification.operations[0].operation='create_subnet'
                $script:applied.effects[0].effect.operation='create_subnet'
                $checks += Check 'TypeIdentifier' 'System:Subnet.Profibus'
                $immediate.checks=$checks
                $script:applied.verification.finalChecks += Check 'subnet/s///TypeIdentifier' 'System:Subnet.Profibus'
            }
            if ('__VARIANT__' -in @('valid-repeat','reordered-batch')) {
                $operations += @{operationId='second';operation='update_subnet';target=@{kind='subnet';subnetId='s'};subnetChanges=@{name='Later'}}
                $next=@{status='passed';identity=@{subnetId='s'};checks=@((Check 'subnetIdentity' 's'),(Check 'networkDeviceCountUnchanged' '2'),(Check 'Name' 'Later'))}
                $script:applied.batch.operations += @{operationId='second';operation='update_subnet';status='succeeded';failure=$null;omission=$null;result=@{subnetId='s';name='Later';networkDeviceCount=2;networkDeviceCountUnchanged=$true;verification=$next}}
                $script:applied.verification.operations += @{operationId='second';operation='update_subnet';status='passed';evidence=$next;omission=$null}
                $script:applied.effects += @{operationId='second';effect=@{operation='update_subnet';target=@{subnetId='s'};affectedNodes=@();rootDeviceCount=2;connectionsComplete=$true};omission=$null}
                $script:applied.verification.finalChecks[2]=Check 'subnet/s///Name' 'Later'
                $script:post.subnets[0].name='Later'
            }
            if ('__VARIANT__' -in @('valid-delete','valid-update-delete','missing-affected-final','wrong-affected-post')) {
                $delete=@{operationId='delete';operation='delete_subnet';target=@{kind='subnet';subnetId='s'}}
                $deleteEvidence=@{status='passed';identity=@{subnetId='s'};checks=@((Check 'networkDeviceCountUnchanged' '2'),(Check 'subnetAbsent' 'true'),(Check 'affectedNodesPreserved' 'true'),(Check 'affectedConnectionsRemoved' 'true'))}
                $deleteItem=@{operationId='delete';operation='delete_subnet';status='succeeded';failure=$null;omission=$null;result=@{subnetId='s';name='After';networkDeviceCount=2;networkDeviceCountUnchanged=$true;verification=$deleteEvidence}}
                $deleteVerification=@{operationId='delete';operation='delete_subnet';status='passed';evidence=$deleteEvidence;omission=$null}
                $deleteEffect=@{operationId='delete';effect=@{operation='delete_subnet';target=@{subnetId='s'};affectedNodes=@(@{deviceName='a';nodeId='n'});rootDeviceCount=2;connectionsComplete=$true};omission=$null}
                if ('__VARIANT__' -eq 'valid-update-delete') {
                    $operations += $delete; $script:applied.batch.operations += $deleteItem
                    $script:applied.verification.operations += $deleteVerification; $script:applied.effects += $deleteEffect
                } else {
                    $operations=@($delete); $script:applied.batch.operations=@($deleteItem)
                    $script:applied.verification.operations=@($deleteVerification); $script:applied.effects=@($deleteEffect)
                }
                $script:applied.verification.finalChecks=@((Check 'networkDeviceCountUnchanged' '2'),(Check 'subnet/s///absent' 'true'),(Check 'node/a//n/exists' 'true'),(Check 'node/a//n/removedSubnet:s' 'true'))
                $script:post.subnets=@()
                $script:postNodes=@(@{deviceName='a';identity=@{deviceName='a';nodeId='n'};node=@{nodeId='n';connectionEvidence=@{complete=$true;subnetId=$null;ioSystemSubnetId=$null}}})
            }
            $script:preview.effects=$script:applied.effects
            switch ('__VARIANT__') {
                'duplicate-batch' { $script:applied.batch.operations += $script:applied.batch.operations[0] }
                'duplicate-immediate' { $script:applied.verification.operations += $script:applied.verification.operations[0] }
                'null-checks' { $immediate.checks=$null }
                'missing-immediate' { $script:applied.verification.operations=@() }
                'wrong-immediate-id' { $script:applied.verification.operations[0].operationId='other' }
                'wrong-immediate-status' { $script:applied.verification.operations[0].status='not_required' }
                'omitted-verification' { $script:applied.verification.omission=@{reason='omitted'} }
                'wrong-final-id' { $script:applied.verification.finalChecks[1].name='subnet/other///exists' }
                'wrong-post-address' { $script:attributeAddress=31 }
                'wrong-post-speed' { $script:attributeSpeed='Baud187500' }
                'null-post-attribute' { $script:nullAttribute=$true }
                'duplicate-post-attribute' { $script:duplicateAttribute=$true }
                'missing-affected-final' { $script:applied.verification.finalChecks=@($script:applied.verification.finalChecks | Where-Object name -ne 'node/a//n/exists') }
                'wrong-affected-post' { $script:postNodes[0].node.connectionEvidence.subnetId='s' }
                'reordered-batch' { $script:applied.batch.operations=@($script:applied.batch.operations[1],$script:applied.batch.operations[0]) }
                'empty-verification' { $script:applied.verification.operations=@(); $script:applied.verification.finalChecks=@() }
                'null-immediate' { $script:applied.batch.operations[0].result.verification=$null }
                'missing-batch' { $script:applied.batch.operations=@() }
                'wrong-operation-id' { $script:applied.batch.operations[0].operationId='other' }
                'wrong-operation' { $script:applied.batch.operations[0].operation='delete_subnet' }
                'wrong-subnet' { $script:applied.batch.operations[0].result.subnetId='other' }
                'duplicate-final' { $script:applied.verification.finalChecks += $script:applied.verification.finalChecks[0] }
                'missing-final' { $script:applied.verification.finalChecks=@($script:applied.verification.finalChecks | Where-Object name -ne 'subnet/s///HighestAddress') }
                'contradictory-final' { $script:applied.verification.finalChecks[1].observed='false' }
                'wrong-attribute' { $checks[3].expected='31'; $checks[3].observed='31' }
                'wrong-post-name' { $script:post.subnets[0].name='Before' }
            }
            # Roundtrip matches the public canonical response's PSCustomObject/array types.
            $script:applied = $script:applied | ConvertTo-Json -Depth 50 | ConvertFrom-Json
            $script:preview = $script:preview | ConvertTo-Json -Depth 50 | ConvertFrom-Json
            $script:post = $script:post | ConvertTo-Json -Depth 50 | ConvertFrom-Json
            function Invoke-NetworkWritePreview { param($Operations) $script:preview }
            function Invoke-NetworkWriteApply { param($Operations) $script:applied }
            function Read-HardwareConfig { $script:post }
            function Get-HardwareNodes { param($Hardware) $script:postNodes }
            function Invoke-McpToolCall {
                param($Name, $Arguments)
                if ($Name -cne 'network_read') { throw 'No mutation transport permitted.' }
                $attributes=@(@{name='HighestAddress';availability='available';value=@{kind='integer';value=$script:attributeAddress}},@{name='TransmissionSpeed';availability='available';value=@{kind='enum';value=@{symbol=$script:attributeSpeed}}})
                if ($script:nullAttribute) { $attributes[0].value=$null }
                if ($script:duplicateAttribute) { $attributes[1]=$attributes[0] }
                @{ success=$true; batch=@{operations=@(@{operationId='verify-subnet';operation='inspect_network_object';status='succeeded';omission=$null;result=@{target=@{kind='subnet';subnetId='s'};messages=@();attributes=$attributes}})} }
            }
            $rootCount=$null; $rejected=$false; $record=$null
            try { $record=Invoke-LifecycleGroupAndVerify 'synthetic' $operations 2 ([ref]$rootCount) } catch { $rejected=$true; $why=$_.Exception.Message }
            if ('__VARIANT__'.StartsWith('valid', [System.StringComparison]::Ordinal)) {
                if ($rejected) { throw "Valid evidence rejected: $why" }
                if (-not $record.Contains('applied') -or ($record.applied | ConvertTo-Json -Depth 50 -Compress) -cne ($script:applied | ConvertTo-Json -Depth 50 -Compress)) { throw 'Canonical applied evidence was not retained.' }
            } elseif (-not $rejected) { throw 'Malformed verification accepted: __VARIANT__' }
            'lifecycle-evidence-ok'
            """.Replace("__VARIANT__", variant, StringComparison.Ordinal));
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("lifecycle-evidence-ok", result.StandardOutput.Trim());
    }


    [Fact]
    public void QualifiedAffectedNodes_RequireBothOriginalE1OwnerChecks()
    {
        var result = RunStaticAstAssertion("""
            foreach ($definition in $helperAst.FindAll({param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst]},$true)) { Invoke-Expression $definition.Extent.Text }
            foreach ($definition in $ast.FindAll({param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -in @('Assert-LifecycleEvidence','Assert-LifecycleChecks')},$true)) { Invoke-Expression $definition.Extent.Text }
            function Check($name,$expected) { @{name=$name;expected=$expected;observed=$expected;status='passed';message=$null} }
            $operations=@(@{operationId='delete';operation='delete_subnet';target=@{subnetId='s'}})
            $affected=@'
            [{"deviceName":"PLC_1","nodeId":"E1","interfacePath":[{"name":"CPU","positionNumber":1},{"name":"X1","positionNumber":32768}]},{"deviceName":"PLC_1","nodeId":"E1","interfacePath":[{"name":"CPU","positionNumber":1},{"name":"X2","positionNumber":33024}]}]
            '@ | ConvertFrom-Json
            $immediate=@{status='passed';identity=@{subnetId='s'};checks=@((Check 'networkDeviceCountUnchanged' '1'),(Check 'subnetAbsent' 'true'),(Check 'affectedNodesPreserved' 'true'),(Check 'affectedConnectionsRemoved' 'true'))}
            $effects=@(@{operationId='delete';effect=@{operation='delete_subnet';target=@{subnetId='s'};affectedNodes=$affected;rootDeviceCount=1;connectionsComplete=$true};omission=$null})
            $checks=@((Check 'networkDeviceCountUnchanged' '1'),(Check 'subnet/s///absent' 'true'))
            foreach($node in $affected) {
                $owner=$node.interfacePath | ConvertTo-Json -Depth 10 -Compress
                $checks += Check "node/PLC_1/$owner/E1/exists" 'true'
                $checks += Check "node/PLC_1/$owner/E1/removedSubnet:s" 'true'
            }
            $applied=@{success=$true;omission=$null;effects=$effects;batch=@{operations=@(@{operationId='delete';operation='delete_subnet';status='succeeded';failure=$null;omission=$null;result=@{subnetId='s';name='Before';verification=$immediate}})};verification=@{success=$true;omission=$null;operations=@(@{operationId='delete';operation='delete_subnet';status='passed';omission=$null;evidence=$immediate});finalChecks=$checks}}
            $applied=$applied | ConvertTo-Json -Depth 40 | ConvertFrom-Json
            $preview=@{effects=$effects} | ConvertTo-Json -Depth 40 | ConvertFrom-Json
            $null=Assert-LifecycleEvidence $applied $operations $preview 1
            $applied.verification.finalChecks=@($applied.verification.finalChecks | Select-Object -SkipLast 1)
            $rejected=$false
            try{$null=Assert-LifecycleEvidence $applied $operations $preview 1}catch{$rejected=$true}
            if(-not $rejected){throw 'Missing second E1 removal check accepted.'}
            'both-qualified-affected-ok'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("both-qualified-affected-ok", result.StandardOutput.Trim());
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.slnx")))
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

            $helperAst = [System.Management.Automation.Language.Parser]::ParseFile(
                {{PowerShellLiteral(FindRepositoryFile("scripts", "network-live-mcp-helpers.ps1"))}}, [ref] $tokens, [ref] $parseErrors)
            if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
            $safeHelpers = @('Get-NetworkMember','ConvertTo-NetworkInterfacePath','ConvertTo-NetworkPathJson','Get-NetworkNodeKey','Test-NetworkNodeIdentity','Get-NetworkNodeCheckName','Assert-HardwareWriteEvidence','Assert-NetworkFixtureHash')
            foreach ($definition in $helperAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -in $safeHelpers }, $true)) { Invoke-Expression $definition.Extent.Text }
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
