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

    [Fact]
    public void SyntheticRunner_EnforcesTimeoutWhileBothStreamsAreOpen()
    {
        var watch = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => RunHarnessFunctions([], "Start-Sleep -Seconds 4", timeoutMilliseconds: 500));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "Synthetic child cleanup exceeded its bound.");
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
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Synthetic harness test did not exit within 30 seconds.");
            }

            return new ScriptResult(process.ExitCode, standardOutput, standardError);
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
