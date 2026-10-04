using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
namespace TiaMcpServer.Tests.Network;
public sealed class NetworkGuardedWriteLiveHarnessScriptTests
{
    private static string EntryPointSource => File.ReadAllText(FindRepositoryFile("scripts", "live-test-network-guarded-write.ps1"));
    private static string SharedSource => File.ReadAllText(FindRepositoryFile("scripts", "network-live-mcp-helpers.ps1"));
    private static string Source => EntryPointSource + "\n" + SharedSource;
    [Fact]
    public void InventoryAndPreview_NeverMutate()
    {
        var script = Source;
        Assert.Contains("$Mode = 'Inventory'", script);
        Assert.Contains("dryRun = $true", script);
        Assert.Contains("'Inventory' { Invoke-Inventory }", script);
        Assert.Contains("'Preview' { Invoke-Preview }", script);
        Assert.Contains("Unexpected server elicitation", script);
    }
    [Fact]
    public void Apply_RequiresExactTargetAuthorization()
    {
        var script = Source;
        Assert.Contains("if (-not $AllowMutation)", script);
        Assert.Contains("$Acknowledgement -cne", script);
        Assert.Contains("$AuthorizedProjectPath -cne $ProjectPath", script);
        Assert.Contains("$AuthorizedFixtureSha256 -ine", script);
        Assert.Contains("Assert-FrozenCandidate", script);
        Assert.Contains("bind_project", script);
        Assert.Contains("$AccessMode = 'read-write'", script);
    }
    [Fact]
    public void PublicCalls_UseDryRunAndNoTokens()
    {
        var script = Source;
        Assert.Contains("dryRun = $false", script);
        Assert.DoesNotContain("safetyToken", script);
        Assert.DoesNotMatch(new Regex(@"(?i)\bconfirm\s*="), script);
        Assert.Contains("earlierOperationFailed", script);
        Assert.Contains("immediateChecks", script);
        Assert.Contains("ExpectedItemStatuses", script);
    }
    [Fact]
    public void Restoration_UsesFreshIdentityInspection()
    {
        var script = Source;
        Assert.Contains("inspect_network_object", script);
        Assert.Contains("Read-FixtureIdentities", script);
        Assert.Contains("RestoreOperations", script);
        Assert.Contains("RestorationExpected", script);
        Assert.Contains("No automatic restoration after a failed or uncertain write", script);
        Assert.DoesNotContain("-Name 'save_project'", script);
        Assert.DoesNotContain("-Name 'close_project'", script);
        Assert.DoesNotContain("-Name 'compile_check'", script);
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
    public void Restoration_InspectsBeforePreviewAndAfterWriteWithoutLaunchingHarness()
    {
        var result = RunStaticAstAssertion("""
            $definition = @($ast.FindAll({ param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Invoke-Apply'
            }, $true))
            Invoke-Expression $definition[0].Extent.Text
            $Restore = $true
            $fixture = @{ BeforeRestore = 'before-restore'; BeforeRestoreNodes = 'before-nodes'; RestorationExpected = 'restored'; RestorationNodes = 'restored-nodes' }
            $script:Evidence = @{ hardware = 'initial'; inspections = 'initial' }
            $script:trace = [System.Collections.Generic.List[string]]::new()
            function Invoke-Inventory { $script:trace.Add('fresh-inventory') }
            function Assert-Inspections($Observed, $Expected) { $script:trace.Add($Expected) }
            function Assert-NodeExpectations($Hardware, $Expected) { $script:trace.Add($Expected) }
            function Invoke-LifecycleGroupAndVerify { $script:trace.Add('preview-then-write') }
            function Read-HardwareConfig { $script:trace.Add('fresh-hardware'); return 'after' }
            function Read-FixtureIdentities { $script:trace.Add('fresh-identities'); return 'after' }
            Invoke-Apply
            if (($script:trace -join ',') -cne 'fresh-inventory,before-restore,before-nodes,preview-then-write,fresh-hardware,fresh-identities,restored,restored-nodes') {
                throw 'Restoration inspection ordering changed.'
            }
            $script:trace.Clear()
            function Assert-Inspections($Observed, $Expected) { throw 'uncertain-inspection' }
            $rejected = $false
            try { Invoke-Apply } catch { $rejected = $_.Exception.Message -eq 'uncertain-inspection' }
            if (-not $rejected -or $script:trace.Contains('preview-then-write')) { throw 'Uncertain inspection reached a write.' }
            'restoration-static-order-ok'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("restoration-static-order-ok", result.StandardOutput.Trim());
    }

    [Fact]
    public void TypedPartialOutcome_RetainsSparseSettingsAndRejectsUncertainEvidence()
    {
        var result = RunStaticAstAssertion("""
            $definition = @($ast.FindAll({ param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -in @('Assert-Outcome', 'Assert-Subset', 'Assert-VerificationCheck')
            }, $true))
            foreach ($function in $definition) { Invoke-Expression $function.Extent.Text }
            $operations = @(@{ operationId = 'first'; operation = 'configure_network_device'; target = @{ deviceName = 'PC'; nodeId = 'node' }; changes = @{ ipAddress = '192.0.2.1'; pnDeviceName = 'requested' } }, @{ operationId = 'later' })
            $evidence = @{ status = 'passed'; identity = @{ deviceName = 'PC'; nodeId = 'node'; interfacePath = '[{"name":"X1","positionNumber":1}]' }; message = $null; checks = @(@{ name = 'Address'; status = 'passed'; expected = '192.0.2.1'; observed = '192.0.2.1'; message = $null }) }
            $response = @{ contractVersion = '1.0'; phase = 'applied'; error = $null; success = $false; omission = $null
                batch = @{ operations = @(
                    @{ operationId = 'first'; operation = 'configure_network_device'; status = 'failed'; omission = $null; result = @{ deviceName = 'PC'; verification = $evidence; appliedSettings = @{ Address = '192.0.2.1' }; skippedSettings = @{ PnDeviceName = 'unavailable' } } },
                    @{ operationId = 'later'; status = 'skipped'; skipReason = 'earlierOperationFailed'; omission = $null }) }
                verification = @{ success = $true; omission = $null; finalChecks = @(@{ name = 'node/PC/[{"name":"X1","positionNumber":1}]/node/exists'; status = 'passed'; expected = 'true'; observed = 'true'; message = $null }, @{ name = 'node/PC/[{"name":"X1","positionNumber":1}]/node/Address'; status = 'passed'; expected = '192.0.2.1'; observed = '192.0.2.1'; message = $null }); operations = @(@{ operationId = 'first'; operation = 'configure_network_device'; status = 'passed'; evidence = $evidence; omission = $null }) }
            } | ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20
            $expected = @(@{ operationId = 'first'; appliedSettings = @{ Address = '192.0.2.1' }; skippedSettings = @{ PnDeviceName = 'unavailable' } })
            Assert-Outcome $response @('failed', 'skipped') $false $true $expected
            $response.verification.operations[0].status = 'unverified'
            $rejected = $false
            try { Assert-Outcome $response @('failed', 'skipped') $false $true $expected } catch { $rejected = $true }
            if (-not $rejected) { throw 'Unverified execution accepted.' }
            $response.verification.operations[0].status = 'passed'
            $response.batch.operations[1].skipReason = 'other'
            $rejected = $false
            try { Assert-Outcome $response @('failed', 'skipped') $false $true $expected } catch { $rejected = $true }
            if (-not $rejected) { throw 'Wrong sequential stop evidence accepted.' }
            'partial-static-outcome-ok'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("partial-static-outcome-ok", result.StandardOutput.Trim());
    }
    [Theory]
    [InlineData("empty-immediate")]
    [InlineData("missing-immediate")]
    [InlineData("duplicate-immediate")]
    [InlineData("wrong-id")]
    [InlineData("wrong-order")]
    [InlineData("missing-evidence")]
    [InlineData("wrong-node")]
    [InlineData("wrong-device")]
    [InlineData("wrong-operation")]
    [InlineData("summary-mismatch")]
    [InlineData("missing-applied-check")]
    [InlineData("duplicate-applied-check")]
    [InlineData("wrong-applied-check")]
    [InlineData("result-evidence-mismatch")]
    [InlineData("not-required-with-applied")]
    [InlineData("empty-final")]
    [InlineData("missing-final")]
    [InlineData("duplicate-final")]
    [InlineData("wrong-final-name")]
    [InlineData("wrong-final-expected")]
    [InlineData("unreadable-final")]
    [InlineData("contradictory-final-status")]
    [InlineData("wrong-check-type")]
    [InlineData("valid-io-tuple")]
    [InlineData("wrong-io-subnet")]
    [InlineData("wrong-identity-type")]
    [InlineData("wrong-field-casing")]
    [InlineData("valid-device-casing")]
    [InlineData("wrong-node-casing")]
    [InlineData("valid-effective-prefix")]
    [InlineData("valid-qualified-two-E1")]
    [InlineData("valid-not-required")]
    public void Outcome_RequiresCompleteTypedVerificationCoverage(string scenario)
    {
        var result = RunStaticAstAssertion($$"""
            $definitions = @($ast.FindAll({ param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -in @('Assert-Outcome', 'Assert-Subset', 'Assert-VerificationCheck')
            }, $true))
            foreach ($definition in $definitions) { Invoke-Expression $definition.Extent.Text }
            $operations = @(
                @{ operationId = 'first'; operation = 'configure_network_device'; target = @{ deviceName = 'PC'; nodeId = 'node' }; changes = @{ ipAddress = '192.0.2.1' } },
                @{ operationId = 'second'; operation = 'configure_network_device'; target = @{ deviceName = 'PC'; nodeId = 'node' }; changes = @{ ipAddress = '192.0.2.2' } })
            $items = @(); $verificationItems = @(); $expected = @()
            foreach ($operation in $operations) {
                $settings = @{ Address = $operation.changes.ipAddress }
                $evidence = @{ status = 'passed'; identity = @{ deviceName = 'PC'; nodeId = 'node'; interfacePath = '[{"name":"X1","positionNumber":1}]' }; message = $null
                    checks = @(@{ name = 'Address'; status = 'passed'; expected = $settings.Address; observed = $settings.Address; message = $null }) }
                $items += @{ operationId = $operation.operationId; operation = $operation.operation; status = 'succeeded'; omission = $null
                    result = @{ deviceName = 'PC'; appliedSettings = $settings; skippedSettings = @{}; verification = $evidence } }
                $verificationItems += @{ operationId = $operation.operationId; operation = $operation.operation; status = 'passed'; evidence = $evidence; omission = $null }
                $expected += @{ operationId = $operation.operationId; appliedSettings = $settings; skippedSettings = @{} }
            }
            $response = @{ contractVersion = '1.0'; phase = 'applied'; error = $null; success = $true; omission = $null
                batch = @{ operations = $items }
                verification = @{ success = $true; omission = $null; operations = $verificationItems; finalChecks = @(
                    @{ name = 'node/PC/[{"name":"X1","positionNumber":1}]/node/exists'; status = 'passed'; expected = 'true'; observed = 'true'; message = $null },
                    @{ name = 'node/PC/[{"name":"X1","positionNumber":1}]/node/Address'; status = 'passed'; expected = '192.0.2.2'; observed = '192.0.2.2'; message = $null }) } }
            $response = $response | ConvertTo-Json -Depth 30 | ConvertFrom-Json -Depth 30
            $scenario = '{{scenario}}'
            switch ($scenario) {
                { $_ -in @('valid-io-tuple','wrong-io-subnet') } {
                    $operations = @($operations[0]); $operations[0].changes = @{ ioSystem = @{ subnetId = 'exact-subnet'; number = 1 } }
                    $response.batch.operations = @($response.batch.operations[0]); $response.verification.operations = @($response.verification.operations[0])
                    $response.batch.operations[0].result.appliedSettings = [pscustomobject]@{ IoSystem = '1' }
                    $response.verification.operations[0].evidence.checks = @([pscustomobject]@{ name = 'IoSystem'; status = 'passed'; expected = '["exact-subnet",1]'; observed = '["exact-subnet",1]'; message = $null })
                    $response.batch.operations[0].result.verification = $response.verification.operations[0].evidence
                    $response.verification.finalChecks[1].name = 'node/PC/[{"name":"X1","positionNumber":1}]/node/IoSystem'; $response.verification.finalChecks[1].expected = '["exact-subnet",1]'; $response.verification.finalChecks[1].observed = '["exact-subnet",1]'
                    $expected = @(@{ operationId = 'first'; appliedSettings = @{ IoSystem = '1' }; skippedSettings = @{} })
                    if ($scenario -eq 'wrong-io-subnet') { $response.verification.operations[0].evidence.checks[0].expected = '["other",1]'; $response.verification.operations[0].evidence.checks[0].observed = '["other",1]' }
                }
                'valid-qualified-two-E1' {
                    for ($i=0;$i -lt 2;$i++) {
                        $path=@(@{name='CPU';positionNumber=1},@{name="X$($i+1)";positionNumber=(32768+256*$i)})
                        $operations[$i].target.nodeId='E1'; $operations[$i].target.interfacePath=$path; $operations[$i].target.interfaceName="X$($i+1)"
                        $response.verification.operations[$i].evidence.identity=[pscustomobject]@{deviceName='PC';nodeId='E1';interfacePath=($path | ConvertTo-Json -Depth 10 -Compress);interfaceName="X$($i+1)"}
                        $response.batch.operations[$i].result.verification=$response.verification.operations[$i].evidence
                    }
                    $response.verification.finalChecks=@()
                    for ($i=0;$i -lt 2;$i++) {
                        $identity=$response.verification.operations[$i].evidence.identity
                        foreach($field in @('exists','Address')) {
                            $value=if($field -eq 'exists'){'true'}else{$operations[$i].changes.ipAddress}
                            $response.verification.finalChecks+=[pscustomobject]@{name=(Get-NetworkNodeCheckName $identity $field);status='passed';expected=$value;observed=$value;message=$null}
                        }
                    }
                }
                'wrong-identity-type' { $operations[0].target.nodeId = '123'; $response.verification.operations[0].evidence.identity.nodeId = 123; $response.batch.operations[0].result.verification.identity.nodeId = 123 }
                'wrong-field-casing' { $response.verification.operations[0].evidence.checks[0].name = 'address' }
                'empty-immediate' { $response.verification.operations = @() }
                'missing-immediate' { $response.verification.operations = @($response.verification.operations[0]) }
                'duplicate-immediate' { $response.verification.operations = @($response.verification.operations[0], $response.verification.operations[0]) }
                'wrong-id' { $response.verification.operations[0].operationId = 'other' }
                'wrong-order' { $response.verification.operations = @($response.verification.operations[1], $response.verification.operations[0]) }
                'valid-device-casing' { $operations[1].target.deviceName = 'pc'; $response.batch.operations[1].result.deviceName = 'pc'; $response.batch.operations[1].result.verification.identity.deviceName = 'pc'; $response.verification.operations[1].evidence.identity.deviceName = 'pc' }
                'wrong-node-casing' { $response.verification.operations[0].evidence.identity.nodeId = 'NODE' }
                'missing-evidence' { $response.verification.operations[0].evidence = $null }
                'wrong-node' { $response.verification.operations[0].evidence.identity.nodeId = 'other' }
                'wrong-device' { $response.verification.operations[0].evidence.identity.deviceName = 'other' }
                'wrong-operation' { $response.verification.operations[0].operation = 'delete_subnet' }
                'summary-mismatch' { $response.verification.operations[0].status = 'failed' }
                'missing-applied-check' { $response.verification.operations[0].evidence.checks = @() }
                'duplicate-applied-check' { $response.verification.operations[0].evidence.checks = @($response.verification.operations[0].evidence.checks[0], $response.verification.operations[0].evidence.checks[0]) }
                'wrong-applied-check' { $response.verification.operations[0].evidence.checks[0].name = 'Subnet' }
                'result-evidence-mismatch' { $response.batch.operations[0].result.verification.identity.nodeId = 'other' }
                'not-required-with-applied' { $response.verification.operations[0].status = 'not_required'; $response.verification.operations[0].evidence.status = 'not_required' }
                'empty-final' { $response.verification.finalChecks = @() }
                'missing-final' { $response.verification.finalChecks = @($response.verification.finalChecks[0]) }
                'duplicate-final' { $response.verification.finalChecks = @($response.verification.finalChecks[0], $response.verification.finalChecks[0]) }
                'wrong-final-name' { $response.verification.finalChecks[1].name = 'node/PC/[{"name":"X1","positionNumber":1}]/other/Address' }
                'wrong-final-expected' { $response.verification.finalChecks[1].expected = '192.0.2.1' }
                'unreadable-final' { $response.verification.finalChecks[1].observed = $null }
                'contradictory-final-status' { $response.verification.finalChecks[1].observed = 'wrong' }
                'wrong-check-type' { $response.verification.operations[0].evidence.checks[0].expected = 123 }
                'valid-not-required' {
                    $operations = @($operations[0]); $operations[0].changes = @{ pnDeviceName = 'requested' }
                    $response.success = $false; $response.batch.operations = @($response.batch.operations[0])
                    $response.batch.operations[0].status = 'failed'; $response.batch.operations[0].result.appliedSettings = [pscustomobject]@{}
                    $response.batch.operations[0].result.skippedSettings = [pscustomobject]@{ PnDeviceName = 'unavailable' }
                    $response.verification.operations = @($response.verification.operations[0]); $response.verification.operations[0].status = 'not_required'
                    $response.verification.operations[0].evidence.status = 'not_required'; $response.verification.operations[0].evidence.checks = @()
                    $response.batch.operations[0].result.verification = $response.verification.operations[0].evidence
                    $response.verification.finalChecks = @($response.verification.finalChecks[0])
                    $expected = @(@{ operationId = 'first'; appliedSettings = @{}; skippedSettings = @{ PnDeviceName = 'unavailable' } })
                }
            }
            if ($scenario -eq 'valid-not-required') { $statuses = @('failed'); $expectedSuccess = $false }
            else { $statuses = @($operations | ForEach-Object { 'succeeded' }); $expectedSuccess = $true }
            $rejected = $false
            try { Assert-Outcome $response $statuses $expectedSuccess $true $expected } catch { $rejected = $true }
            if ($scenario -like 'valid-*') {
                if ($rejected) { throw "Valid verification rejected: $scenario" }
            } elseif (-not $rejected) { throw "Incomplete or contradictory verification accepted: $scenario" }
            'verification-coverage-ok'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("verification-coverage-ok", result.StandardOutput.Trim());
    }

    [Fact]
    public void BothHarnesses_ShareFrozenProtocolAndDiscoveryOwner()
    {
        var helperPath = FindRepositoryFile("scripts", "network-live-mcp-helpers.ps1");
        Assert.True(File.Exists(helperPath), "Shared Network harness helper is missing.");
        var helper = File.ReadAllText(helperPath);
        foreach (var entrypoint in new[] { EntryPointSource, File.ReadAllText(FindRepositoryFile("scripts", "live-test-network-phase4-subnets.ps1")) })
        {
            Assert.Contains("ExpectedSharedHelperSha256", entrypoint);
            Assert.Contains(". $script:SharedHelperPath", entrypoint);
            Assert.DoesNotContain("function Read-HardwareConfig", entrypoint);
            Assert.DoesNotContain("function Start-McpHost", entrypoint);
            Assert.DoesNotContain("function Get-HardwareNodes", entrypoint);
        }
        foreach (var name in new[] { "Start-McpHost", "Read-McpResponse", "Get-ObservedProjectStatus", "Read-HardwareConfig", "Get-HardwareNodes", "Assert-FrozenCandidate" })
            Assert.Contains("function " + name, helper);
        Assert.DoesNotContain("function Invoke-NetworkWriteApply", helper);
        Assert.DoesNotContain("Plain-string tools (get_project_status)", helper);
    }
    [Theory]
    [InlineData("live-test-network-guarded-write.ps1")]
    [InlineData("live-test-network-phase4-subnets.ps1")]
    public void SharedHelper_LoadGateRejectsMissingAndChangedSourceBeforeDotSourcing(string entrypoint)
    {
        var result = RunStaticAstAssertion("""
            $guard = @($ast.FindAll({ param($node)
                $node -is [System.Management.Automation.Language.IfStatementAst] -and
                $node.Extent.Text -match 'Test-Path.*SharedHelperPath' -and
                $node.Extent.Text -match 'Get-FileHash.*SharedHelperPath' -and
                $node.Extent.Text -match 'ExpectedSharedHelperSha256'
            }, $true))
            if ($guard.Count -ne 1) { throw 'Missing shared-helper existence/hash load guard.' }
            $loads = @($ast.FindAll({ param($node)
                $node -is [System.Management.Automation.Language.CommandAst] -and
                $node.InvocationOperator -eq [System.Management.Automation.Language.TokenKind]::Dot -and
                $node.Extent.Text -ceq '. $script:SharedHelperPath'
            }, $true))
            if ($loads.Count -ne 1 -or $loads[0].Extent.StartOffset -le $guard[0].Extent.EndOffset) { throw 'Shared helper loads before source guard.' }
            $script:SharedHelperPath = Join-Path ([System.IO.Path]::GetTempPath()) ('missing-helper-' + [guid]::NewGuid() + '.ps1')
            $ExpectedSharedHelperSha256 = '00'
            $rejected = $false
            try { Invoke-Expression $guard[0].Extent.Text } catch { $rejected = $true }
            if (-not $rejected) { throw 'Missing helper was accepted.' }
            $script:SharedHelperPath = Join-Path (Split-Path $ast.Extent.File) 'network-live-mcp-helpers.ps1'
            $rejected = $false
            try { Invoke-Expression $guard[0].Extent.Text } catch { $rejected = $true }
            if (-not $rejected) { throw 'Changed helper hash was accepted.' }
            $ExpectedSharedHelperSha256 = (Get-FileHash -LiteralPath $script:SharedHelperPath -Algorithm SHA256).Hash
            Invoke-Expression $guard[0].Extent.Text
            'shared-helper-load-guard-ok'
            """, entrypoint);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("shared-helper-load-guard-ok", result.StandardOutput.Trim());
    }
    [Fact]
    public void SharedHelper_HasNoTopLevelActionsAndFreezesHelperHashAndCleanPath()
    {
        var result = RunStaticAstAssertion("""
            foreach ($statement in $helperAst.EndBlock.Statements) {
                if ($statement -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { throw 'Shared helper contains a top-level action.' }
            }
            $definition = @($helperAst.FindAll({ param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Assert-FrozenCandidate'
            }, $true))
            Invoke-Expression $definition[0].Extent.Text
            $script:RepositoryRoot = 'C:\synthetic'
            $script:SharedHelperPath = $helperAst.Extent.File
            $harnessPath = $ast.Extent.File
            $harnessHash = (Get-FileHash -LiteralPath $harnessPath -Algorithm SHA256).Hash
            $helperHash = (Get-FileHash -LiteralPath $script:SharedHelperPath -Algorithm SHA256).Hash
            $script:dirty = $false
            $script:statusArgs = @()
            function git {
                $global:LASTEXITCODE = 0
                if ($args -contains 'rev-parse') {
                    if ($args -contains 'HEAD^{tree}') { return 'tree' }
                    return 'commit'
                }
                $script:statusArgs = $args
                if ($script:dirty) { return ' M scripts/network-live-mcp-helpers.ps1' }
            }
            $candidate = Assert-FrozenCandidate -ExpectedCommit 'commit' -ExpectedTree 'tree' -ExpectedHarnessSha256 $harnessHash -HarnessPath $harnessPath -ExpectedSharedHelperSha256 $helperHash
            if ($candidate.testedSharedHelperSha256 -cne $helperHash.ToLowerInvariant() -or
                $script:statusArgs -notcontains 'scripts/network-live-mcp-helpers.ps1') { throw 'Shared source provenance is missing.' }
            $script:dirty = $true
            $rejected = $false
            try { Assert-FrozenCandidate -ExpectedCommit 'commit' -ExpectedTree 'tree' -ExpectedHarnessSha256 $harnessHash -HarnessPath $harnessPath -ExpectedSharedHelperSha256 $helperHash | Out-Null } catch { $rejected = $_.Exception.Message -match 'dirty' }
            if (-not $rejected) { throw 'Dirty shared helper was accepted.' }
            $script:dirty = $false
            $rejected = $false
            try { Assert-FrozenCandidate -ExpectedCommit 'commit' -ExpectedTree 'tree' -ExpectedHarnessSha256 $harnessHash -HarnessPath $harnessPath -ExpectedSharedHelperSha256 'changed' | Out-Null } catch { $rejected = $true }
            if (-not $rejected) { throw 'Changed shared hash was accepted.' }
            'shared-helper-frozen-source-ok'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("shared-helper-frozen-source-ok", result.StandardOutput.Trim());
    }

    // Offline captures only: these tests extract function definitions and replace every transport.
    private const string QualifiedHelperSetup = """
        foreach ($definition in @($helperAst.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
            Invoke-Expression $definition.Extent.Text
        }
        foreach ($definition in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -in @('Assert-NodeExpectations','Assert-Subset','Read-FixtureIdentities','Invoke-NetworkWriteApply') }, $true))) {
            Invoke-Expression $definition.Extent.Text
        }
        $ProjectPath = 'C:/offline-fixture.ap21'
        $capture = @'
        {"discoveryEvidence":{"scope":"project","complete":true,"failures":[]},"rootDeviceCount":1,"messages":[],"subnets":[],"devices":[{"name":"PLC_1","items":[{"name":"CPU","networkInterfaces":[],"items":[
          {"name":"X2","positionNumber":33024,"items":[],"networkInterfaces":[{"name":"X2","nodes":[{"nodeId":"E1","selectable":true,"selector":{"kind":"node","deviceName":"PLC_1","itemPath":null,"interfacePath":[{"name":"CPU","positionNumber":1},{"name":"X2","positionNumber":33024}],"interfaceName":"X2","nodeId":"E1","nodeIndex":0},"connectionEvidence":{"complete":true,"subnetId":"original-X2","ioSystemSubnetId":null,"ioSystemNumber":null},"ipAddress":"192.0.2.2"}]}]},
          {"name":"X1","positionNumber":32768,"items":[],"networkInterfaces":[{"name":"X1","nodes":[{"nodeId":"E1","selectable":true,"selector":{"kind":"node","deviceName":"PLC_1","itemPath":null,"interfacePath":[{"name":"CPU","positionNumber":1},{"name":"X1","positionNumber":32768}],"interfaceName":"X1","nodeId":"E1","nodeIndex":0},"connectionEvidence":{"complete":true,"subnetId":"original-X1","ioSystemSubnetId":null,"ioSystemNumber":null},"ipAddress":"192.0.2.1"}]}]}
        ]}]}]}
        '@ | ConvertFrom-Json -Depth 40
        $originalSelectors = @($capture.devices[0].items[0].items | ForEach-Object { $_.networkInterfaces[0].nodes[0].selector })
        $fixture = @{ MultiHomedDevices=@('plc_1'); Inspections=@(); RestoreOperations=@() }
        """;

    [Fact]
    public void QualifiedInventory_KeepsBothE1Nodes()
    {
        var result = RunStaticAstAssertion(QualifiedHelperSetup + "\n" + """
            $nodes = @(Get-HardwareNodes $capture)
            if ($nodes.Count -ne 2) { throw 'Both original E1 nodes must survive.' }
            $ownerPaths = @($nodes | ForEach-Object { $_.selector.interfacePath | ConvertTo-Json -Depth 15 -Compress })
            if ($ownerPaths[0] -ceq $ownerPaths[1]) { throw 'Owner paths collapsed.' }
            for ($i=0; $i -lt 2; $i++) {
                if (($nodes[$i].selector | ConvertTo-Json -Depth 15 -Compress) -cne ($originalSelectors[$i] | ConvertTo-Json -Depth 15 -Compress)) { throw 'Original public selector was changed or sorted indices inferred.' }
            }
            $capture.messages = @('Optional type identifier unavailable.')
            if (@(Get-HardwareNodes $capture).Count -ne 2) { throw 'Optional diagnostics were treated as traversal loss.' }
            'qualified-inventory-ok'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("qualified-inventory-ok", result.StandardOutput.Trim());
    }

    [Fact]
    public void InspectionAndRestoration_UseOriginalOwnerSelectors()
    {
        var result = RunStaticAstAssertion(QualifiedHelperSetup + "\n" + """
            $expectations = @()
            foreach ($selector in $originalSelectors) {
                $target = $selector | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
                $fixture.Inspections += @{ id=$selector.interfaceName; target=$target }
                $fixture.RestoreOperations += @{ operationId=$selector.interfaceName; operation='configure_network_device'; projectPath=$ProjectPath; target=$target; changes=@{subnet=@{subnetId="original-$($selector.interfaceName)"}} }
                $expectations += @{ deviceName='plc_1'; nodeId='E1'; interfacePath=$target.interfacePath; interfaceName=$target.interfaceName; subnetId="original-$($selector.interfaceName)"; ioSystemSubnetId=$null; ioSystemNumber=$null }
            }
            $script:requests=@()
            function Invoke-McpToolCall {
                param($Name,$Arguments)
                if ($Name -cne 'network_read') { throw 'Offline inspection only.' }
                $script:requests += $Arguments.operations[0]
                @{ success=$true; batch=@{operations=@(@{status='succeeded';omission=$null;result=@{target=$Arguments.operations[0].target}})} }
            }
            $null=Read-FixtureIdentities
            $null=Assert-NodeExpectations $capture $expectations
            for ($i=0;$i -lt 2;$i++) {
                $original=$originalSelectors[$i] | ConvertTo-Json -Depth 20 -Compress
                if (($script:requests[$i].target | ConvertTo-Json -Depth 20 -Compress) -cne ($fixture.RestoreOperations[$i].target | ConvertTo-Json -Depth 20 -Compress)) { throw 'Restoration changed owner selector.' }
                Assert-Subset $script:requests[$i].target ($original | ConvertFrom-Json -AsHashtable)
            }
            $capture.devices[0].items[0].items[1].networkInterfaces[0].nodes[0].connectionEvidence.subnetId='wrong-restored'
            $rejected=$false
            try { $null=Assert-NodeExpectations $capture $expectations } catch { $rejected=$true }
            if (-not $rejected) { throw 'Wrong restored X1 tuple accepted.' }
            'original-selectors-ok'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("original-selectors-ok", result.StandardOutput.Trim());
    }

    [Theory]
    [InlineData("[{\"name\":\"X1\",\"positionNumber\":\"32768\"}]")]
    [InlineData("[{\"name\":\"X1\",\"positionNumber\":32768,\"positionNumber\":33024}]")]
    [InlineData("[{\"name\":\"X1\",\"positionNumber\":32768,\"unknown\":1}]")]
    [InlineData("[{\"name\":\"X1\",\"positionNumber\":-1}]")]
    [InlineData("[]")]
    public void MalformedQualifiedIdentity_IsRejected(string encodedPath)
    {
        var result = RunStaticAstAssertion(QualifiedHelperSetup + "\n" + $$"""
            $capture.devices[0].items[0].items[0].networkInterfaces[0].nodes[0].selector.interfacePath = {{PowerShellLiteral(encodedPath)}}
            $rejected=$false
            try { $null=Get-HardwareNodes $capture } catch { $rejected=$true }
            if (-not $rejected) { throw 'Malformed qualified identity accepted.' }
            # Supplemental strict scalar decoder assertion, alongside original behavioral RED.
            $rejected=$false
            try { $null=ConvertTo-NetworkPathJson {{PowerShellLiteral(encodedPath)}} } catch { $rejected=$true }
            if (-not $rejected) { throw 'Malformed canonical scalar identity accepted.' }
            'malformed-qualified-rejected'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("malformed-qualified-rejected", result.StandardOutput.Trim());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("incomplete")]
    [InlineData("page")]
    public void UnknownTraversalEvidence_BlocksApply(string variant)
    {
        var result = RunStaticAstAssertion(QualifiedHelperSetup + "\n" + $$"""
            $operations=@($fixture.RestoreOperations)
            $fixtureSha256=$null; $FixturePath=$null
            $capture.discoveryEvidence = {{(variant == "unknown" ? "$null" : "[pscustomobject]@{scope='project';complete=$false;failures=@()}")}}
            if ('{{variant}}' -eq 'page') {
                $capture.discoveryEvidence=[pscustomobject]@{scope='project';complete=$true;failures=@()}
                $capture | Add-Member pagination @{totalDevices=1;returnedDevices=1}
            }
            $script:toolCalls=0
            function Assert-NetworkFixtureHash { } # This regression isolates traversal, not the independently tested file gate.
            function Read-HardwareConfig { $capture }
            function Invoke-McpToolCall { param($Name,$Arguments) $script:toolCalls++; return @{} }
            $rejected=$false
            try { $null=Invoke-NetworkWriteApply } catch { $rejected=$_.Exception.Message -like '*ordinary project traversal*' }
            if (-not $rejected -or $script:toolCalls -ne 0) { throw 'Unknown/incomplete/paged evidence reached apply callback.' }
            'unknown-traversal-blocked'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("unknown-traversal-blocked", result.StandardOutput.Trim());
    }

    [Fact]
    public void FixtureHashChange_StopsBeforeToolCall()
    {
        var result = RunStaticAstAssertion(QualifiedHelperSetup + "\n" + """
            $FixturePath=Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N')+'.json')
            try {
                '{}' | Set-Content -LiteralPath $FixturePath
                $fixtureSha256=(Get-FileHash -LiteralPath $FixturePath -Algorithm SHA256).Hash
                $operations=@(); $script:toolCalls=0; $script:readCalls=0
                function Read-HardwareConfig { $script:readCalls++; return $capture }
                function Invoke-McpToolCall { param($Name,$Arguments) $script:toolCalls++; return @{} }
                $null=Invoke-NetworkWriteApply
                if($script:toolCalls -ne 1){throw 'Unchanged authorized fixture did not reach harmless callback.'}
                '{"changed":true}' | Set-Content -LiteralPath $FixturePath
                $rejected=$false
                try { $null=Invoke-NetworkWriteApply } catch { $rejected=$true }
                if (-not $rejected -or $script:toolCalls -ne 1 -or $script:readCalls -ne 1) { throw 'Changed fixture reached tool callback.' }
            } finally { Remove-Item -LiteralPath $FixturePath -Force }
            'fixture-hash-blocked'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("fixture-hash-blocked", result.StandardOutput.Trim());
    }


    // Supplemental post-RED coverage for ordinary reads and constraint/equality boundaries.
    [Fact]
    public void OrdinaryHardwareRead_RetainsEvidenceAndNeverSynthesizesItFromPages()
    {
        var result = RunStaticAstAssertion(QualifiedHelperSetup + "\n" + """
            $script:requests=@()
            function Invoke-McpToolCall {
                param($Name,$Arguments)
                $script:requests+=$Arguments.operations[0]
                @{success=$true;batch=@{operations=@(@{status='succeeded';result=$capture})}}
            }
            $observed=Read-HardwareConfig
            Assert-HardwareWriteEvidence $observed
            if($script:requests[0].ContainsKey('pageSize') -or $script:requests[0].ContainsKey('cursor')){throw 'Guarded evidence read was paged.'}
            $capture.discoveryEvidence=$null
            $observed=Read-HardwareConfig
            $rejected=$false
            try{Assert-HardwareWriteEvidence $observed}catch{$rejected=$true}
            if(-not $rejected){throw 'Missing ordinary evidence synthesized.'}
            'ordinary-hardware-evidence-ok'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("ordinary-hardware-evidence-ok", result.StandardOutput.Trim());
    }

    [Fact]
    public void QualifiedIdentity_UsesOrdinalOwnersAndRetainsOptionalConstraints()
    {
        var result = RunStaticAstAssertion(QualifiedHelperSetup + "\n" + """
            $nodes=@(Get-HardwareNodes $capture)
            $expected=$originalSelectors[0] | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
            $expected.deviceName='plc_1'
            if(-not (Test-NetworkNodeIdentity $expected $nodes[0].identity)){throw 'Device casing rejected.'}
            $legacy=@{deviceName='plc_1';nodeId='E1'}
            if(@($nodes | Where-Object{Test-NetworkNodeIdentity $legacy $_.identity}).Count -ne 2){throw 'Bare E1 arbitrarily chose one owner.'}
            $legacy.interfaceName='X2'
            if(@($nodes | Where-Object{Test-NetworkNodeIdentity $legacy $_.identity}).Count -ne 1){throw 'Legacy exact interface constraint was lost.'}
            $baseKey=Get-NetworkNodeKey $expected
            $expected.interfacePath[1].typeIdentifier='OptionalType'
            if((Get-NetworkNodeKey $expected) -cne $baseKey){throw 'Optional type entered equality.'}
            if(Test-NetworkNodeIdentity $expected $nodes[0].identity){throw 'Unreadable required type constraint accepted.'}
            $expected.interfacePath[1].Remove('typeIdentifier')
            $expected.interfacePath[1].name='x2'
            if((Get-NetworkNodeKey $expected) -ceq $baseKey -or (Test-NetworkNodeIdentity $expected $nodes[0].identity)){throw 'Owner casing was normalized.'}
            $expected.interfacePath[1].name='X2';$expected.nodeId='e1'
            if(Test-NetworkNodeIdentity $expected $nodes[0].identity){throw 'Node casing was normalized.'}
            'ordinal-qualified-identity-ok'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("ordinal-qualified-identity-ok", result.StandardOutput.Trim());
    }


    // Supplemental self-review causal RED: local restoration assertions need ordinary evidence too.
    [Fact]
    public void UnknownRestorationTraversal_IsRejected()
    {
        var result = RunStaticAstAssertion(QualifiedHelperSetup + "\n" + """
            $capture.discoveryEvidence=$null
            $rejected=$false
            try{$null=Assert-NodeExpectations $capture @()}catch{$rejected=$true}
            if(-not $rejected){throw 'Unknown ordinary traversal accepted as restoration evidence.'}
            'unknown-restoration-blocked'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("unknown-restoration-blocked", result.StandardOutput.Trim());
    }


    // Supplemental self-review causal RED for read compatibility with legacy selectors.
    [Fact]
    public void LegacyInventory_PreservesDuplicateBareIdsAndExactConstraints()
    {
        var result = RunStaticAstAssertion(QualifiedHelperSetup + "\n" + """
            for($i=0;$i -lt 2;$i++) {
                $selector=$originalSelectors[$i]
                $selector.PSObject.Properties.Remove('interfacePath')
                $selector.itemPath=@([pscustomobject]@{index=(1-$i);name=$selector.interfaceName;positionNumber=(33024-256*$i);typeIdentifier='LegacyPort'})
            }
            $nodes=@(Get-HardwareNodes $capture)
            if($nodes.Count -ne 2){throw 'Duplicate bare IDs were treated as invalid hardware.'}
            for($i=0;$i -lt 2;$i++) {
                Assert-Subset $nodes[$i].selector ($originalSelectors[$i] | ConvertTo-Json -Depth 15 | ConvertFrom-Json -AsHashtable)
            }
            $expected=$originalSelectors[0] | ConvertTo-Json -Depth 15 | ConvertFrom-Json -AsHashtable
            $expected.itemPath[0].index=99
            $expectation=@{deviceName='PLC_1';nodeId='E1';interfaceName='X2';itemPath=$expected.itemPath;subnetId='original-X2';ioSystemSubnetId=$null;ioSystemNumber=$null}
            $rejected=$false
            try{$null=Assert-NodeExpectations $capture @($expectation)}catch{$rejected=$true}
            if(-not $rejected){throw 'Legacy source-index constraint was ignored.'}
            'legacy-inventory-constraints-ok'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("legacy-inventory-constraints-ok", result.StandardOutput.Trim());
    }


    // Supplemental causal RED: unqualified read rows must never become write map keys.
    [Fact]
    public void UnqualifiedWriteIdentity_HasNoMapKey()
    {
        var result = RunStaticAstAssertion(QualifiedHelperSetup + "\n" + """
            $rejected=$false
            try{$null=Get-NetworkNodeKey @{deviceName='PLC_1';nodeId='E1'}}catch{$rejected=$true}
            if(-not $rejected){throw 'Unqualified device/node identity became a write map key.'}
            'unqualified-map-key-rejected'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("unqualified-map-key-rejected", result.StandardOutput.Trim());
    }


    [Theory]
    [InlineData("Preview", false, "dispatch")]
    [InlineData("Apply", false, "dispatch")]
    [InlineData("Apply", true, "dispatch")]
    [InlineData("Preview", false, "mode")]
    [InlineData("Apply", false, "mode")]
    [InlineData("Apply", true, "mode")]
    public void FixtureHashChange_StopsBeforeAllModeDispatchCallbacks(string mode, bool restore, string route)
    {
        var result = RunStaticAstAssertion(QualifiedHelperSetup + "\n" + """
            # Extract actual mode functions and the actual common try-body. Never execute the entrypoint.
            foreach($definition in $ast.FindAll({param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -in @('Invoke-Inventory','Invoke-Preview','Invoke-Apply')},$true)) {
                Invoke-Expression $definition.Extent.Text
            }
            $dispatch=@($ast.EndBlock.Statements | Where-Object {
                $_ -is [System.Management.Automation.Language.TryStatementAst] -and $_.Body.Extent.Text -like '*Connect-McpHost*'
            })
            if($dispatch.Count -ne 1){throw 'Expected one common mode dispatch body.'}
            $dispatchBody=[scriptblock]::Create(($dispatch[0].Body.Statements | ForEach-Object {$_.Extent.Text}) -join "`n")
            $fixture=@{ BeforeExpected=@{};BeforeNodes=@();BeforeRestore=@{};BeforeRestoreNodes=@();AfterExpected=@{};AfterNodes=@();RestorationExpected=@{};RestorationNodes=@() }
            $script:trace=[System.Collections.Generic.List[string]]::new()
            # Every potential preliminary/verification/write callback is replaced and counted.
            function Connect-McpHost {$script:trace.Add('connect')}
            function Get-ObservedProjectStatus {$script:trace.Add('status');return @{}}
            function Read-HardwareConfig {$script:trace.Add('hardware');return @{}}
            function Read-FixtureIdentities {$script:trace.Add('identities');return @{}}
            function Assert-Inspections {param($Observed,$Expected)}
            function Assert-NodeExpectations {param($Hardware,$Expected)}
            function Invoke-NetworkWritePreview {
                Assert-NetworkFixtureHash $FixturePath $fixtureSha256
                $script:trace.Add('preview');return @{}
            }
            function Invoke-LifecycleGroupAndVerify {
                $null=Invoke-NetworkWritePreview
                $script:trace.Add('apply')
            }
            $FixturePath=Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N')+'.json')
            try {
                '{}' | Set-Content -LiteralPath $FixturePath
                $fixtureSha256=(Get-FileHash -LiteralPath $FixturePath -Algorithm SHA256).Hash
                $Mode='__MODE__';$Restore=$__RESTORE__;$script:Evidence=@{}
                # Positive control: the required unchanged fixture reaches the actual selected route.
                if('__ROUTE__' -eq 'dispatch'){. $dispatchBody}
                elseif($Mode -eq 'Preview'){Invoke-Preview}else{Invoke-Apply}
                if(-not $script:trace.Contains('hardware') -or -not $script:trace.Contains('identities') -or -not $script:trace.Contains('preview')) {
                    throw 'Unchanged fixture did not reach harmless mode callbacks.'
                }
                $script:trace.Clear();$script:Evidence=@{}
                '{"changed":true}' | Set-Content -LiteralPath $FixturePath
                $rejected=$false
                try {
                    if('__ROUTE__' -eq 'dispatch'){. $dispatchBody}
                    elseif($Mode -eq 'Preview'){Invoke-Preview}else{Invoke-Apply}
                } catch {$rejected=$_.Exception.Message -like '*Fixture changed since authorization*'}
                if(-not $rejected -or $script:trace.Count -ne 0) {
                    throw "Changed fixture reached preliminary __MODE__/__ROUTE__ callbacks: $($script:trace -join ',')."
                }
                # Inventory retains its separate read-only gate despite the changed reviewed file.
                $Mode='Inventory';$Restore=$false;$script:Evidence=@{}
                . $dispatchBody
                if(($script:trace -join ',') -cne 'connect,status,hardware,identities'){throw 'Inventory gate was changed.'}
            } finally {Remove-Item -LiteralPath $FixturePath -Force}
            'mode-dispatch-fixture-gate-ok'
            """.Replace("__MODE__", mode, StringComparison.Ordinal)
                .Replace("__ROUTE__", route, StringComparison.Ordinal)
                .Replace("__RESTORE__", restore.ToString().ToLowerInvariant(), StringComparison.Ordinal));
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("mode-dispatch-fixture-gate-ok", result.StandardOutput.Trim());
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

    private static ScriptResult RunStaticAstAssertion(string assertionBody, string harnessName = "live-test-network-guarded-write.ps1")
    {
        var harnessPath = FindRepositoryFile("scripts", harnessName);
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
