using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

// Ordinary tests never invoke the live harness body; helper tests execute only isolated functions.
public sealed class WritePreviewDiffLiveHarnessContractTests
{
    private static readonly string ScriptPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "live-test-preview-write-diff.ps1"));

    [Fact]
    public void Script_ExistsAndRequiresPowerShell7()
    {
        var text = ReadScript();
        Assert.Matches(new Regex(@"^\s*#Requires\s+-Version\s+7(\.\d+)?\s*$", RegexOptions.Multiline), text);
        Assert.Contains("$ErrorActionPreference = 'Stop'", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_DefaultsToPreviewAndGuardsApplyBehindAnExplicitSwitch()
    {
        var text = ReadScript();
        Assert.Matches(new Regex(@"\[ValidateSet\(\s*'Preview'\s*,\s*'Apply'\s*\)\]"), text);
        Assert.Matches(new Regex(@"\[string\]\s*\$Mode\s*=\s*'Preview'"), text);
        Assert.Matches(new Regex(@"\[switch\]\s*\$AllowApply"), text);
        Assert.Matches(new Regex(@"if\s*\(\s*\$Mode\s*-eq\s*'Apply'\s*-and\s*-not\s*\$AllowApply\s*\)"), text);
        Assert.Contains("[switch] $ConfirmApplyForCi", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_UsesTheRealHostLevelMcpProtocolRatherThanDirectWorkerIpc()
    {
        var text = ReadScript();
        foreach (var required in new[] { "TiaMcpServer.dll", "--project", "'initialize'",
                     "notifications/initialized", "'tools/call'", "get_project_status",
                     "execute_read_batch", "preview_write_batch", "apply_write_batch" })
            Assert.Contains(required, text, StringComparison.Ordinal);
        Assert.DoesNotContain("OpennessWorker.exe", text, StringComparison.Ordinal);
        Assert.DoesNotContain("open_project", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ApplyPathRestoresOriginalBytesAndCompilesTheDisposableProject()
    {
        var text = ReadScript();
        Assert.Contains("compile_check", text, StringComparison.Ordinal);
        Assert.Contains("byte-identical", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restore", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Read-Host", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_SourceHashUsesLowercaseHexForCaseSensitivePreviewComparison()
    {
        var text = ReadScript();
        var hashFunction = Regex.Match(text,
            @"(?ms)^function Get-TextHash\([^\r\n]+\) \{\r?\n(?<body>.*?)^\}");
        Assert.True(hashFunction.Success, "Expected the source hash helper.");
        Assert.Contains(
            "return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes($Text))).ToLowerInvariant()",
            hashFunction.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains(".current.sha256 -ceq (Get-TextHash $original[$i])", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_DocumentsDisposableProjectScopeAndWritesTheAcceptanceReportPath()
    {
        var text = ReadScript();
        Assert.Matches(new Regex(@"disposable", RegexOptions.IgnoreCase), text);
        Assert.Contains("2026-09-01-pr4-structured-preview-diff-live.md", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Script_ProjectVersionEvidenceUsesExplicitFallbackWithoutRunningHarness()
    {
        const string fixture = """
            param([Parameter(Mandatory)] [string] $HarnessPath)

            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $tokens = $null
            $parseErrors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile(
                $HarnessPath,
                [ref] $tokens,
                [ref] $parseErrors)
            if ($parseErrors.Count -ne 0) {
                throw "Harness parsing failed: $($parseErrors[0].Message)"
            }

            foreach ($functionName in @('Assert-Condition', 'Read-Binding')) {
                $functionAst = $ast.Find({
                        param($node)
                        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] `
                            -and $node.Name -eq $functionName
                    }, $true)
                if ($null -eq $functionAst) {
                    throw "Function '$functionName' was not found."
                }
                Invoke-Expression $functionAst.Extent.Text
            }

            $ProjectPath = Join-Path ([IO.Path]::GetTempPath()) 'preview-diff-fixture.ap21'
            $script:InitialIdentity = $null
            $evidence = @{ Binding = ''; TiaVersion = '' }
            $script:ProjectVersion = $null
            function Invoke-Tool {
                $payload = @{
                    success = $true
                    projectPath = $ProjectPath
                    project = @{ isOpen = $true; path = $ProjectPath; version = $script:ProjectVersion }
                } | ConvertTo-Json -Compress -Depth 10
                return @{
                    success = $true
                    payload = $payload
                    sessionIdentity = @{
                        projectPath = $ProjectPath
                        workerSessionId = 'fixture-worker'
                        sessionGeneration = 1
                        portalProcessId = 42
                    }
                }
            }
            $cases = @(
                @{ name = 'missing'; value = $null; expected = 'V21 prerequisite; project version not reported' },
                @{ name = 'blank'; value = '   '; expected = 'V21 prerequisite; project version not reported' },
                @{ name = 'reported'; value = 'V21 Update 4'; expected = 'V21 prerequisite; project version reported: V21 Update 4' }
            )
            foreach ($case in $cases) {
                $script:ProjectVersion = $case.value
                $null = Read-Binding
                $actual = $evidence.TiaVersion
                if ($actual -cne $case.expected) {
                    throw "Unexpected $($case.name) evidence: '$actual'."
                }
                if ($actual -notmatch '^[\x20-\x7E]+$' -or $actual -match '\s$') {
                    throw "Evidence must be nonblank ASCII without trailing whitespace: '$actual'."
                }
            }
            Write-Output 'tia-version-evidence-ok'
            """;

        var result = await RunPowerShellFixtureAsync(fixture);

        Assert.True(result.ExitCode == 0,
            $"PowerShell fixture failed with exit code {result.ExitCode}: {result.StandardError}");
        Assert.Contains("tia-version-evidence-ok", result.StandardOutput, StringComparison.Ordinal);
    }

    private static string ReadScript()
    {
        Assert.True(File.Exists(ScriptPath), $"Expected the live harness at '{ScriptPath}'.");
        return File.ReadAllText(ScriptPath);
    }

    private static async Task<PowerShellResult> RunPowerShellFixtureAsync(string fixture)
    {
        var fixturePath = Path.Combine(Path.GetTempPath(), $"preview-diff-harness-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(fixturePath, fixture + Environment.NewLine);
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", fixturePath, "-HarnessPath", ScriptPath })
            startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            Assert.True(process.Start(), "Expected the PowerShell fixture process to start.");
            var standardOutput = await process.StandardOutput.ReadToEndAsync();
            var standardError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new PowerShellResult(process.ExitCode, standardOutput, standardError);
        }
        finally
        {
            File.Delete(fixturePath);
        }
    }

    private sealed record PowerShellResult(int ExitCode, string StandardOutput, string StandardError);
}
