using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectTreeLiveHarnessContractTests
{
    [Fact]
    public void LiveHarness_IsReadOnlyAndCoversTheApprovedEvidenceMatrix()
    {
        var source = File.ReadAllText(RepositoryFile("scripts", "live-test-project-tree-v3.ps1"));
        Assert.Contains("--read-only", source, StringComparison.Ordinal);
        Assert.Contains("1..3", source, StringComparison.Ordinal);
        Assert.Contains("Measure-InitialBrowse", source, StringComparison.Ordinal);
        Assert.Contains("Read-AllSnapshotPages", source, StringComparison.Ordinal);
        Assert.Contains("Reconstruct-TypedSelector", source, StringComparison.Ordinal);
        Assert.Contains("target_not_found", source, StringComparison.Ordinal);
        Assert.Contains("target_ambiguous", source, StringComparison.Ordinal);
        Assert.Contains("invalid_selector", source, StringComparison.Ordinal);
        Assert.Contains("Assert-CanonicalRepresentationsEqual", source, StringComparison.Ordinal);
        Assert.Contains("ConvertTo-Json", source, StringComparison.Ordinal);
        Assert.Contains("project-tree-v3-evidence.json", source, StringComparison.Ordinal);

        foreach (var writeToolName in new[]
        {
            "apply_write_batch",
            "archive_project",
            "close_project",
            "compile_check",
            "create_project",
            "network_write",
            "open_project",
            "preview_write_batch",
            "save_project",
            "save_project_as",
        })
        {
            Assert.DoesNotContain(writeToolName, source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("confirm=true", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SnapshotVerification_AcceptsEmptyDetailsObject()
    {
        var result = RunHarnessFunctions(
            ["Assert-Condition", "Assert-CanonicalRepresentationsEqual", "Assert-SucceededResponse", "Assert-CompleteSnapshotEvidence"],
            """
            $page = [pscustomobject]@{
                contractVersion = '3.0'
                status = 'succeeded'
                result = [pscustomobject]@{
                    snapshot = [pscustomobject]@{ snapshotId = 'snapshot-1'; totalNodes = 1 }
                    pagination = [pscustomobject]@{ offset = 0; returnedCount = 1; nextCursor = $null }
                    nodes = @([pscustomobject]@{
                        nodeId = 'node-1'
                        parentNodeId = $null
                        sequence = 0
                        name = 'PLC_1'
                        nodeType = 'Device'
                        details = [pscustomobject]@{}
                    })
                }
                failure = $null
                warnings = @()
                __contentText = '{}'
                __structuredJson = '{}'
            }

            $evidence = Assert-CompleteSnapshotEvidence -Mode 'synthetic' -Pages @($page)
            if (-not $evidence.legacyPathAbsent) { throw 'Empty details were not accepted.' }
            if ($evidence.maximumCanonicalResponseChars -ne 2) { throw 'Canonical page length was not measured.' }
            Write-Output 'empty-details-ok'
            """);

        Assert.True(result.ExitCode == 0, $"PowerShell failed. stdout: {result.StandardOutput}{Environment.NewLine}stderr: {result.StandardError}");
        Assert.Equal("empty-details-ok", result.StandardOutput.Trim());
    }

    [Fact]
    public void AmbiguousSelector_DiscoversDuplicateDeviceRoots()
    {
        var result = RunHarnessFunctions(
            ["Assert-Condition", "Reconstruct-TypedSelector", "Find-AmbiguousSelector"],
            """
            $nodes = @(
                [pscustomobject]@{ nodeId = 'device-1'; parentNodeId = $null; nodeType = 'Device'; name = 'PLC_1' },
                [pscustomobject]@{ nodeId = 'device-2'; parentNodeId = $null; nodeType = 'Device'; name = 'plc_1' }
            )

            $selector = @(Find-AmbiguousSelector -Nodes $nodes)
            if ($selector.Count -ne 1) { throw "Expected one selector segment, received $($selector.Count)." }
            if ([string] $selector[0].nodeType -cne 'Device') { throw 'Expected a Device selector.' }
            if ([string] $selector[0].name -cne 'PLC_1') { throw 'Expected the first observed Device name.' }
            Write-Output 'root-ambiguity-ok'
            """);

        Assert.True(result.ExitCode == 0, $"PowerShell failed. stdout: {result.StandardOutput}{Environment.NewLine}stderr: {result.StandardError}");
        Assert.Equal("root-ambiguity-ok", result.StandardOutput.Trim());
    }

    [Theory]
    [InlineData(60_000, false)]
    [InlineData(60_001, true)]
    public void CanonicalPageBudget_EnforcesExactInclusiveLimit(int length, bool mustReject)
    {
        var result = RunHarnessFunctions(
            ["Assert-Condition", "Assert-CanonicalRepresentationsEqual"],
            $$"""
            $canonical = '"' + ('x' * ({{length}} - 2)) + '"'
            $response = [pscustomobject]@{ contractVersion = '3.0'; __contentText = $canonical; __structuredJson = $canonical }
            $rejected = $false
            try { Assert-CanonicalRepresentationsEqual $response } catch { $rejected = $true }
            if ($rejected -ne ${{mustReject.ToString().ToLowerInvariant()}}) { throw 'Canonical length limit was not enforced exactly.' }
            'canonical-budget-ok'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MeasurementDevice_SkipsAmbiguousAndNonPlcRoots(bool ambiguousFirst)
    {
        var result = RunHarnessFunctions(
            ["Assert-Condition", "Get-NodeDepth", "Get-DeepestNode"],
            $$"""
            foreach ($definition in $ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -in @('Find-EligibleDevice', 'Test-UniqueNodeSelector')
            }, $true)) { Invoke-Expression $definition.Extent.Text }
            function Node($id, $parent, $type, $name, $sequence) {
                [pscustomobject]@{ nodeId = $id; parentNodeId = $parent; nodeType = $type; name = $name; sequence = $sequence }
            }
            $fullNodes = @(
                Node 'first' $null 'Device' 'First' 0
                {{(ambiguousFirst ? "Node 'duplicate' $null 'Device' 'FIRST' 1\nNode 'first-software' 'first' 'PlcSoftware' 'Program' 2\nNode 'first-folder' 'first-software' 'BlockFolder' 'Blocks' 3" : "")}}
                Node 'plc' $null 'Device' 'Eligible' 10
                Node 'software' 'plc' 'PlcSoftware' 'Program' 11
                Node 'folder' 'software' 'BlockFolder' 'Blocks' 12
                Node 'block' 'folder' 'FB' 'SafeTarget' 13
                Node 'ambiguous1' 'folder' 'FB' 'Duplicate' 14
                Node 'ambiguous2' 'folder' 'FB' 'DUPLICATE' 15
            )
            # Execute only the actual top-level device-selection assignment, never the harness.
            $assignment = @($ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left.Extent.Text -ceq '$deviceNode'
            }, $true))
            if ($assignment.Count -ne 1) { throw 'Expected exactly one device-selection assignment.' }
            Invoke-Expression $assignment[0].Extent.Text
            if ($deviceNode.nodeId -cne 'plc') { throw 'Did not select the later uniquely addressable eligible PLC.' }
            $deviceNodes = @($fullNodes | Where-Object { $_.sequence -ge 10 })
            $deep = Get-DeepestNode -Nodes $deviceNodes
            if ($deep.nodeId -cne 'block') { throw 'Deep target is not uniquely addressable.' }
            'eligible-device-ok'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("eligible-device-ok", result.StandardOutput.Trim());
    }

    [Fact]
    public void SyntheticRunner_EnforcesTimeoutWhileBothStreamsAreOpen()
    {
        var watch = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => RunHarnessFunctions([], "Start-Sleep -Seconds 4", timeoutMilliseconds: 500));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "Synthetic child cleanup exceeded its bound.");
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("offset")]
    [InlineData("empty")]
    [InlineData("count")]
    [InlineData("total")]
    [InlineData("premature")]
    public void Pagination_RejectsBrokenProgressBeforeAnotherCall(string defect)
    {
        var result = RunHarnessFunctions(
            ["Assert-Condition", "Assert-CanonicalRepresentationsEqual", "Assert-SucceededResponse", "Read-AllSnapshotPages"],
            $$"""
            $script:calls = 0
            function New-Page($offset, $count, $cursor) {
                [pscustomobject]@{
                    contractVersion = '3.0'; status = 'succeeded'; failure = $null
                    __contentText = '{}'; __structuredJson = '{}'
                    result = [pscustomobject]@{
                        snapshot = [pscustomobject]@{ snapshotId = 's'; totalNodes = 3 }
                        pagination = [pscustomobject]@{ offset = $offset; returnedCount = $count; nextCursor = $cursor }
                        nodes = @('node')
                    }
                }
            }
            function Invoke-McpTool {
                param($Name, $Arguments)
                $script:calls++
                if ($script:calls -gt 1) { throw 'MOCK_CALL_LIMIT' }
                $page = New-Page 1 1 'next'
                switch ('{{defect}}') {
                    'cycle' { $page.result.pagination.nextCursor = 'first' }
                    'offset' { $page.result.pagination.offset = 0 }
                    'empty' { $page.result.pagination.returnedCount = 0; $page.result.nodes = @() }
                    'count' { $page.result.pagination.returnedCount = 2 }
                    'total' { $page.result.snapshot.totalNodes = 4 }
                    'premature' { $page.result.pagination.nextCursor = $null }
                }
                return $page
            }
            $rejected = $false
            try { $null = Read-AllSnapshotPages -FirstPage (New-Page 0 1 'first') }
            catch {
                if ($_.Exception.Message -eq 'MOCK_CALL_LIMIT') { throw }
                $rejected = $true
            }
            if (-not $rejected -or $script:calls -ne 1) { throw 'Broken pagination was not rejected promptly.' }
            'progress-rejected'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("progress-rejected", result.StandardOutput.Trim());
    }

    [Fact]
    public void Pagination_AcceptsCompleteForwardChainAndBoundsOverallCollection()
    {
        var result = RunHarnessFunctions(
            ["Assert-Condition", "Assert-CanonicalRepresentationsEqual", "Assert-SucceededResponse", "Read-AllSnapshotPages"],
            """
            $script:calls = 0
            function New-Page($offset, $cursor) {
                [pscustomobject]@{
                    contractVersion = '3.0'; status = 'succeeded'; failure = $null
                    __contentText = '{}'; __structuredJson = '{}'
                    result = [pscustomobject]@{
                        snapshot = [pscustomobject]@{ snapshotId = 's'; totalNodes = 2 }
                        pagination = [pscustomobject]@{ offset = $offset; returnedCount = 1; nextCursor = $cursor }
                        nodes = @('node')
                    }
                }
            }
            function Invoke-McpTool { param($Name, $Arguments); $script:calls++; New-Page 1 $null }
            $pages = @(Read-AllSnapshotPages -FirstPage (New-Page 0 'next'))
            if ($pages.Count -ne 2 -or $script:calls -ne 1) { throw 'Forward chain failed.' }
            $rejected = $false
            try { $null = Read-AllSnapshotPages -FirstPage (New-Page 0 'next') -MaximumCollectionSeconds 0 }
            catch { $rejected = $true }
            if (-not $rejected -or $script:calls -ne 1) { throw 'Expired overall collection budget made another call.' }
            'collection-bound-ok'
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
    }

    private static ScriptResult RunHarnessFunctions(string[] functionNames, string body, int timeoutMilliseconds = 30_000)
    {
        var scriptPath = RepositoryFile("scripts", "live-test-project-tree-v3.ps1");
        var functionNamesLiteral = string.Join(", ", functionNames.Select(PowerShellLiteral));
        var syntheticScript = $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $tokens = $null
            $parseErrors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile({{PowerShellLiteral(scriptPath)}}, [ref] $tokens, [ref] $parseErrors)
            if ($parseErrors.Count -ne 0) { throw ($parseErrors | ForEach-Object Message | Out-String) }

            foreach ($functionName in @({{functionNamesLiteral}})) {
                $matches = @($ast.FindAll({
                    param($node)
                    ($node -is [System.Management.Automation.Language.FunctionDefinitionAst]) -and
                    ($node.Name -ceq $functionName)
                }, $true))
                if ($matches.Count -ne 1) { throw "Expected one $functionName function, found $($matches.Count)." }
                Invoke-Expression $matches[0].Extent.Text
            }

            {{body}}
            """;

        var syntheticPath = Path.Combine(Path.GetTempPath(), $"project-tree-v3-harness-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(syntheticPath, syntheticScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
                ?? throw new InvalidOperationException("Failed to start pwsh process.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
                throw new TimeoutException($"Synthetic harness test did not exit within {timeoutMilliseconds} milliseconds.");
            }

            if (!Task.WhenAll(standardOutput, standardError).Wait(5_000))
            {
                throw new TimeoutException("Synthetic harness output streams did not close within 5 seconds.");
            }

            return new ScriptResult(process.ExitCode, standardOutput.GetAwaiter().GetResult(), standardError.GetAwaiter().GetResult());
        }
        finally
        {
            File.Delete(syntheticPath);
        }
    }

    private static string PowerShellLiteral(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string RepositoryFile(params string[] pathSegments)
        => Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(pathSegments).ToArray()));

    private sealed record ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
