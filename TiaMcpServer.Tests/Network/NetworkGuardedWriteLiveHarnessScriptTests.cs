using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
namespace TiaMcpServer.Tests.Network;
public sealed class NetworkGuardedWriteLiveHarnessScriptTests
{
    private static string Source
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.slnx")))
                {
                    var path = Path.Combine(directory.FullName, "scripts", "live-test-network-guarded-write.ps1");
                    return File.Exists(path) ? File.ReadAllText(path) : "";
                }
            throw new InvalidOperationException("Repository root not found.");
        }
    }
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
            $evidence = @{ status = 'passed'; identity = @{ deviceName = 'PC'; nodeId = 'node' }; message = $null; checks = @(@{ name = 'Address'; status = 'passed'; expected = '192.0.2.1'; observed = '192.0.2.1'; message = $null }) }
            $response = @{ contractVersion = '1.0'; phase = 'applied'; error = $null; success = $false; omission = $null
                batch = @{ operations = @(
                    @{ operationId = 'first'; operation = 'configure_network_device'; status = 'failed'; omission = $null; result = @{ deviceName = 'PC'; verification = $evidence; appliedSettings = @{ Address = '192.0.2.1' }; skippedSettings = @{ PnDeviceName = 'unavailable' } } },
                    @{ operationId = 'later'; status = 'skipped'; skipReason = 'earlierOperationFailed'; omission = $null }) }
                verification = @{ success = $true; omission = $null; finalChecks = @(@{ name = 'node/PC/node/exists'; status = 'passed'; expected = 'true'; observed = 'true'; message = $null }, @{ name = 'node/PC/node/Address'; status = 'passed'; expected = '192.0.2.1'; observed = '192.0.2.1'; message = $null }); operations = @(@{ operationId = 'first'; operation = 'configure_network_device'; status = 'passed'; evidence = $evidence; omission = $null }) }
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
                $evidence = @{ status = 'passed'; identity = @{ deviceName = 'PC'; nodeId = 'node' }; message = $null
                    checks = @(@{ name = 'Address'; status = 'passed'; expected = $settings.Address; observed = $settings.Address; message = $null }) }
                $items += @{ operationId = $operation.operationId; operation = $operation.operation; status = 'succeeded'; omission = $null
                    result = @{ deviceName = 'PC'; appliedSettings = $settings; skippedSettings = @{}; verification = $evidence } }
                $verificationItems += @{ operationId = $operation.operationId; operation = $operation.operation; status = 'passed'; evidence = $evidence; omission = $null }
                $expected += @{ operationId = $operation.operationId; appliedSettings = $settings; skippedSettings = @{} }
            }
            $response = @{ contractVersion = '1.0'; phase = 'applied'; error = $null; success = $true; omission = $null
                batch = @{ operations = $items }
                verification = @{ success = $true; omission = $null; operations = $verificationItems; finalChecks = @(
                    @{ name = 'node/PC/node/exists'; status = 'passed'; expected = 'true'; observed = 'true'; message = $null },
                    @{ name = 'node/PC/node/Address'; status = 'passed'; expected = '192.0.2.2'; observed = '192.0.2.2'; message = $null }) } }
            $response = $response | ConvertTo-Json -Depth 30 | ConvertFrom-Json -Depth 30
            $scenario = '{{scenario}}'
            switch ($scenario) {
                { $_ -in @('valid-io-tuple','wrong-io-subnet') } {
                    $operations = @($operations[0]); $operations[0].changes = @{ ioSystem = @{ subnetId = 'exact-subnet'; number = 1 } }
                    $response.batch.operations = @($response.batch.operations[0]); $response.verification.operations = @($response.verification.operations[0])
                    $response.batch.operations[0].result.appliedSettings = [pscustomobject]@{ IoSystem = '1' }
                    $response.verification.operations[0].evidence.checks = @([pscustomobject]@{ name = 'IoSystem'; status = 'passed'; expected = '["exact-subnet",1]'; observed = '["exact-subnet",1]'; message = $null })
                    $response.batch.operations[0].result.verification = $response.verification.operations[0].evidence
                    $response.verification.finalChecks[1].name = 'node/PC/node/IoSystem'; $response.verification.finalChecks[1].expected = '["exact-subnet",1]'; $response.verification.finalChecks[1].observed = '["exact-subnet",1]'
                    $expected = @(@{ operationId = 'first'; appliedSettings = @{ IoSystem = '1' }; skippedSettings = @{} })
                    if ($scenario -eq 'wrong-io-subnet') { $response.verification.operations[0].evidence.checks[0].expected = '["other",1]'; $response.verification.operations[0].evidence.checks[0].observed = '["other",1]' }
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
                'wrong-final-name' { $response.verification.finalChecks[1].name = 'node/PC/other/Address' }
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
        foreach (var entrypoint in new[] { Source, File.ReadAllText(FindRepositoryFile("scripts", "live-test-network-phase4-subnets.ps1")) })
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
    [Fact]
    public void SharedHelper_LoadGateRejectsMissingAndChangedSourceBeforeDotSourcing()
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
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("shared-helper-load-guard-ok", result.StandardOutput.Trim());
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
        var harnessPath = FindRepositoryFile("scripts", "live-test-network-guarded-write.ps1");
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
