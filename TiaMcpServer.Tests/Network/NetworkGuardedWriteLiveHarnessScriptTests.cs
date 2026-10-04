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
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -in @('Assert-Outcome', 'Assert-Subset')
            }, $true))
            foreach ($function in $definition) { Invoke-Expression $function.Extent.Text }
            $operations = @(@{ operationId = 'first' }, @{ operationId = 'later' })
            $response = @{ contractVersion = '1.0'; phase = 'applied'; error = $null; success = $false; omission = $null
                batch = @{ operations = @(
                    @{ operationId = 'first'; status = 'failed'; omission = $null; result = @{ appliedSettings = @{ Address = '192.0.2.1' }; skippedSettings = @{ PnDeviceName = 'unavailable' } } },
                    @{ operationId = 'later'; status = 'skipped'; skipReason = 'earlierOperationFailed'; omission = $null }) }
                verification = @{ success = $true; omission = $null; finalChecks = @(@{ status = 'passed' }); operations = @(@{ status = 'passed'; evidence = @{ checks = @(@{ status = 'passed' }) }; omission = $null }) }
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
