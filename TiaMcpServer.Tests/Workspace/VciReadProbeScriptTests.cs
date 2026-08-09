using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Workspace;

public sealed class VciReadProbeScriptTests
{
    private static readonly string RepositoryRoot = GetRepositoryRoot();
    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "scripts",
        "live-probe-vci-phase1-read.ps1");

    [Fact]
    public void Script_IsAsciiPowerShell7StrictAndDefaultsToDescribe()
    {
        var source = ReadScript();

        Assert.Contains("#Requires -Version 7", source, StringComparison.Ordinal);
        Assert.Contains("Set-StrictMode -Version Latest", source, StringComparison.Ordinal);
        Assert.Contains("$ErrorActionPreference = 'Stop'", source, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(
                @"\[ValidateSet\('Describe', 'Run'\)\]\s*\r?\n\s*\[string\]\s*\$Mode\s*=\s*'Describe'",
                RegexOptions.CultureInvariant),
            source);
    }

    [Fact]
    public void Describe_EmitsExactlyOneCompleteInertHarnessDescription()
    {
        var result = RunPowerShell("-File", ScriptPath);

        Assert.True(
            result.ExitCode == 0,
            $"Describe failed with exit code {result.ExitCode}.{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{result.StandardError}");
        Assert.DoesNotContain('\n', result.StandardOutput.TrimEnd('\r', '\n'));

        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.Equal("vci-phase1-read-harness/v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("readOnly").GetBoolean());
        Assert.False(root.GetProperty("mutatesProject").GetBoolean());
        Assert.Equal("probe_vci_read_contract", root.GetProperty("workerOperation").GetString());
        Assert.Equal("read-only", root.GetProperty("workerAccessMode").GetString());
        Assert.True(root.GetProperty("requiresSeparateLiveAuthorization").GetBoolean());
        Assert.Equal(2, root.GetProperty("workerSessions").GetInt32());

        Assert.Equal(
            new[]
            {
                "N-FMT-FOREIGN", "N-FMT-NULL", "N-FMT-UNSUPPORTED",
                "N-GRP-FIND-EMPTY", "N-GRP-FIND-MISSING", "N-GRP-FIND-NULL",
                "N-GRP-FIND-WHITESPACE", "N-MAP-INACCESSIBLE-FILE",
                "N-MAP-MISSING-FILE", "N-WS-FIND-EMPTY", "N-WS-FIND-MISSING",
                "N-WS-FIND-NULL", "N-WS-FIND-WHITESPACE", "R-CANARY", "R-FMT",
                "R-GRP", "R-MAP", "R-REP", "R-SVC", "R-WS",
            },
            root.GetProperty("caseIds")
                .EnumerateArray()
                .Select(item => item.GetString())
                .OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(
            new[]
            {
                "cases.jsonl",
                "filesystem-after.json",
                "filesystem-before.json",
                "manifest.json",
                "snapshot-after.json",
                "snapshot-before.json",
                "summary.json",
            },
            root.GetProperty("evidenceFiles")
                .EnumerateArray()
                .Select(item => item.GetString())
                .OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public void Script_UsesAWorkerOnlyReadOnlyProcessWithInheritedStandardError()
    {
        var source = ReadScript();

        Assert.DoesNotContain("Start-McpHost", source, StringComparison.Ordinal);
        Assert.DoesNotContain("workspace_", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--access-mode", source, StringComparison.Ordinal);
        Assert.Contains("read-only", source, StringComparison.Ordinal);
        Assert.Contains("$psi.RedirectStandardInput = $true", source, StringComparison.Ordinal);
        Assert.Contains("$psi.RedirectStandardOutput = $true", source, StringComparison.Ordinal);
        Assert.Contains("$psi.RedirectStandardError = $false", source, StringComparison.Ordinal);
        Assert.Contains("$psi.UseShellExecute = $false", source, StringComparison.Ordinal);
        Assert.Contains("$psi.CreateNoWindow = $true", source, StringComparison.Ordinal);
        Assert.Contains("ReadLineAsync()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ErrorDataReceived", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OutputDataReceived", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginErrorReadLine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginOutputReadLine", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_HasExactlyOneTopLevelPostDescribeLifecycleWithOrderedTransportAndCleanup()
    {
        var result = RunRunBlockOrderSmokeTest();

        Assert.True(result.ExitCode == 0, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("topLevelRunLifecycleCount").GetInt32());
        Assert.True(
            root.GetProperty("afterDescribe").GetBoolean(),
            "The Run lifecycle must be a top-level statement after the Describe guard.");
        Assert.True(
            root.GetProperty("transportOrdered").GetBoolean(),
            "The top-level Run lifecycle must directly write, flush, bounded-read, then assert the denial.");
        Assert.True(
            root.GetProperty("cleanupInSameFinally").GetBoolean(),
            "The top-level Run lifecycle must close, terminate if needed, and dispose in its own finally block.");
    }

    [Fact]
    public void TransportResponseValidator_AcceptsOnlyTheExpectedReadOnlyDenialEnvelope()
    {
        var result = RunTransportValidatorSmokeTest();

        Assert.True(result.ExitCode == 0, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var outcomes = document.RootElement.EnumerateArray().Select(item => item.GetBoolean()).ToArray();
        Assert.Equal(new[] { true, false, false, false, false, false }, outcomes);
    }

    [Fact]
    public void EvidenceRootPreflight_RejectsAnExistingFileWithoutStartingAWorker()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vci-evidence-root-{Guid.NewGuid():N}");
        var allowedRoot = Path.Combine(root, "artifacts", "live-vci-phase1");
        Directory.CreateDirectory(allowedRoot);
        var evidenceFile = Path.Combine(allowedRoot, "not-a-directory.json");
        File.WriteAllText(evidenceFile, "x");
        try
        {
            var result = RunEvidenceRootSmokeTest(root, allowedRoot, evidenceFile);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("directories", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProcessReader_ReceivesJsonLineAndInheritsStandardErrorWithoutRunspaceCallbacks()
    {
        _ = ReadScript();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var smokeScriptPath = WriteSmokeScript();
            ScriptResult result;
            try
            {
                result = RunPowerShell("-File", smokeScriptPath);
            }
            finally
            {
                File.Delete(smokeScriptPath);
            }

            Assert.True(
                result.ExitCode == 0,
                $"Process reader smoke attempt {attempt + 1} failed with exit code {result.ExitCode}.{Environment.NewLine}" +
                $"stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}" +
                $"stderr:{Environment.NewLine}{result.StandardError}");
            Assert.Contains("stdout-probe", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("stderr-probe", result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain("PSInvalidOperationException", result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain("There is no Runspace available", result.StandardError, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NoOrdinaryTestInvokesTheLiveProbeRunMode()
    {
        var references = Directory.EnumerateFiles(
                RepositoryRoot,
                "*.*",
                SearchOption.AllDirectories)
            .Where(path => new[] { ".cs", ".ps1", ".yml", ".yaml" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + ".superpowers" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !string.Equals(path, ScriptPath, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(nameof(VciReadProbeScriptTests) + ".cs", StringComparison.Ordinal))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("live-probe-vci-phase1-read.ps1", StringComparison.Ordinal) &&
                    source.Contains("-Mode Run", StringComparison.Ordinal);
            })
            .ToArray();

        Assert.Empty(references);
    }

    private static string ReadScript()
    {
        Assert.True(File.Exists(ScriptPath), $"Expected VCI read probe harness at {ScriptPath}.");
        var bytes = File.ReadAllBytes(ScriptPath);
        Assert.All(bytes, value => Assert.InRange(value, (byte)0, (byte)127));
        return Encoding.ASCII.GetString(bytes);
    }

    private static ScriptResult RunPowerShell(params string[] arguments)
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
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start pwsh process.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("PowerShell script test did not exit.");
        }

        return new ScriptResult(
            process.ExitCode,
            standardOutput.GetAwaiter().GetResult(),
            standardError.GetAwaiter().GetResult());
    }

    private static string WriteSmokeScript()
    {
        var smokeScriptPath = Path.Combine(
            Path.GetTempPath(),
            $"vci-read-probe-process-{Guid.NewGuid():N}.ps1");
        var escapedHarnessPath = ScriptPath.Replace("'", "''", StringComparison.Ordinal);
        var source = ProcessReaderSmokeScript.Replace("REPLACE_HARNESS_PATH", escapedHarnessPath, StringComparison.Ordinal);
        File.WriteAllText(smokeScriptPath, source, new UTF8Encoding(false));
        return smokeScriptPath;
    }

    private static ScriptResult RunTransportValidatorSmokeTest()
    {
        var escapedHarnessPath = ScriptPath.Replace("'", "''", StringComparison.Ordinal);
        var command = """
            $harnessPath = 'REPLACE_HARNESS_PATH'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($harnessPath, [ref] $tokens, [ref] $errors)
            $functionAst = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-TransportProbeResponse' }, $true)
            if ($null -eq $functionAst) { throw 'Validator function was not found.' }
            Invoke-Expression $functionAst.Extent.Text
            $fixtures = @(
                '{"success":false,"error":"Operation ''__task7_transport_probe__'' is disabled because the worker is running in read-only mode.","failureCategory":"access_denied"}',
                '{"success":true,"payload":"handled"}',
                '{"success":false,"error":"unexpected","failureCategory":"access_denied"}',
                '{"arbitrary":true}',
                '{not-json',
                '{"success":false,"error":"Operation ''__task7_transport_probe__'' is disabled because the worker is running in read-only mode.","failureCategory":"access_denied","payload":"unexpected"}'
            )
            $outcomes = foreach ($fixture in $fixtures) { try { Test-TransportProbeResponse -ResponseText $fixture; $true } catch { $false } }
            $outcomes | ConvertTo-Json -Compress -Depth 10
            """.Replace("REPLACE_HARNESS_PATH", escapedHarnessPath, StringComparison.Ordinal);
        return RunPowerShell("-Command", command);
    }

    private static ScriptResult RunRunBlockOrderSmokeTest()
    {
        var command = """
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('REPLACE_HARNESS_PATH', [ref] $tokens, [ref] $errors)
            if ($errors.Count -ne 0) { throw 'Harness parsing failed.' }

            function Get-DirectStatementIndex {
                param([object[]] $Statements, [string] $Text)

                $matches = @($Statements | Where-Object { $_.Extent.Text -eq $Text })
                if ($matches.Count -ne 1) { return -1 }
                return [Array]::IndexOf($Statements, $matches[0])
            }

            $topLevelStatements = @($ast.EndBlock.Statements)
            $describeGuards = @($topLevelStatements | Where-Object {
                    $_ -is [System.Management.Automation.Language.IfStatementAst] -and
                    $_.Clauses.Count -eq 1 -and
                    $_.Clauses[0].Item1.Extent.Text -eq '$Mode -eq ''Describe'''
                })
            $runLifecycles = @(
                for ($index = 0; $index -lt $topLevelStatements.Count; $index++) {
                    $statement = $topLevelStatements[$index]
                    if ($statement -isnot [System.Management.Automation.Language.TryStatementAst]) { continue }
                    $bodyStatements = @($statement.Body.Statements)
                    $start = Get-DirectStatementIndex `
                        -Statements $bodyStatements `
                        -Text '$worker = Start-JsonLineProcess -Executable $canonicalWorkerExecutable -Arguments $workerArguments'
                    if ($start -ge 0) {
                        [pscustomobject]@{ Index = $index; Ast = $statement }
                    }
                }
            )

            $afterDescribe = $false
            $transportOrdered = $false
            $cleanupInSameFinally = $false
            if ($describeGuards.Count -eq 1 -and $runLifecycles.Count -eq 1) {
                $describeIndex = [Array]::IndexOf($topLevelStatements, $describeGuards[0])
                $runLifecycle = $runLifecycles[0]
                $runTry = $runLifecycle.Ast
                $afterDescribe = $runLifecycle.Index -gt $describeIndex

                $bodyStatements = @($runTry.Body.Statements)
                $write = Get-DirectStatementIndex -Statements $bodyStatements -Text '$worker.StandardInput.WriteLine($transportProbe)'
                $flush = Get-DirectStatementIndex -Statements $bodyStatements -Text '$worker.StandardInput.Flush()'
                $read = Get-DirectStatementIndex -Statements $bodyStatements -Text '$transportResponse = Read-JsonLine -Process $worker -TimeoutSeconds $TimeoutSeconds'
                $assertDenied = Get-DirectStatementIndex -Statements $bodyStatements -Text 'Test-TransportProbeResponse -ResponseText $transportResponse'
                $transportOrdered = $write -ge 0 -and $write -lt $flush -and $flush -lt $read -and $read -lt $assertDenied

                if ($null -ne $runTry.Finally) {
                    $finallyStatements = @($runTry.Finally.Statements)
                    $cleanupIfs = @($finallyStatements | Where-Object {
                            $_ -is [System.Management.Automation.Language.IfStatementAst] -and
                            $_.Clauses.Count -eq 1 -and
                            $_.Clauses[0].Item1.Extent.Text -eq '$null -ne $worker'
                        })
                    if ($cleanupIfs.Count -eq 1) {
                        $cleanupStatements = @($cleanupIfs[0].Clauses[0].Item2.Statements)
                        $closeTry = @($cleanupStatements | Where-Object {
                                $_ -is [System.Management.Automation.Language.TryStatementAst] -and
                                $_.Body.Statements.Count -eq 1 -and
                                $_.Body.Statements[0].Extent.Text -eq '$worker.StandardInput.Close()'
                            })
                        $terminateIf = @($cleanupStatements | Where-Object {
                                $_ -is [System.Management.Automation.Language.IfStatementAst] -and
                                $_.Clauses.Count -eq 1 -and
                                $_.Clauses[0].Item1.Extent.Text -eq '-not $worker.HasExited' -and
                                $_.Clauses[0].Item2.Statements.Count -eq 1 -and
                                $_.Clauses[0].Item2.Statements[0] -is [System.Management.Automation.Language.TryStatementAst] -and
                                $_.Clauses[0].Item2.Statements[0].Body.Statements.Count -eq 1 -and
                                $_.Clauses[0].Item2.Statements[0].Body.Statements[0].Extent.Text -eq '$worker.Kill($true)'
                            })
                        $close = if ($closeTry.Count -eq 1) { [Array]::IndexOf($cleanupStatements, $closeTry[0]) } else { -1 }
                        $terminate = if ($terminateIf.Count -eq 1) { [Array]::IndexOf($cleanupStatements, $terminateIf[0]) } else { -1 }
                        $dispose = Get-DirectStatementIndex -Statements $cleanupStatements -Text '$worker.Dispose()'
                        $cleanupInSameFinally = $close -ge 0 -and $close -lt $terminate -and $terminate -lt $dispose
                    }
                }
            }

            [ordered]@{
                topLevelRunLifecycleCount = $runLifecycles.Count
                afterDescribe = $afterDescribe
                transportOrdered = $transportOrdered
                cleanupInSameFinally = $cleanupInSameFinally
            } | ConvertTo-Json -Compress -Depth 10
            """.Replace("REPLACE_HARNESS_PATH", ScriptPath.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal);
        return RunPowerShell("-Command", command);
    }

    private static ScriptResult RunEvidenceRootSmokeTest(string root, string allowedRoot, string evidenceFile)
    {
        var command = """
            $harnessPath = 'REPLACE_HARNESS_PATH'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($harnessPath, [ref] $tokens, [ref] $errors)
            foreach ($functionName in @('Test-AbsolutePath', 'Resolve-CanonicalDirectoryPath')) {
                $functionAst = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $functionName }, $true)
                Invoke-Expression $functionAst.Extent.Text
            }
            Resolve-CanonicalDirectoryPath -Path 'REPLACE_EVIDENCE_FILE' -RepositoryRoot 'REPLACE_ROOT' -AllowedRoot 'REPLACE_ALLOWED_ROOT'
            """
            .Replace("REPLACE_HARNESS_PATH", ScriptPath.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal)
            .Replace("REPLACE_EVIDENCE_FILE", evidenceFile.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal)
            .Replace("REPLACE_ROOT", root.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal)
            .Replace("REPLACE_ALLOWED_ROOT", allowedRoot.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal);
        return RunPowerShell("-Command", command);
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private const string ProcessReaderSmokeScript = """
        param()

        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'

        $harnessPath = 'REPLACE_HARNESS_PATH'
        $tokens = $null
        $parseErrors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile(
            $harnessPath,
            [ref] $tokens,
            [ref] $parseErrors)
        if ($parseErrors.Count -ne 0) {
            throw 'Harness parsing failed.'
        }

        foreach ($functionName in @('Start-JsonLineProcess', 'Read-JsonLine')) {
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

        $pwshPath = (Get-Command pwsh).Source
        $childCommand = '[Console]::Error.WriteLine(''stderr-probe''); [Console]::Out.WriteLine(''{"kind":"stdout-probe"}'')'
        $process = Start-JsonLineProcess `
            -Executable $pwshPath `
            -Arguments @('-NoProfile', '-Command', $childCommand)
        try {
            $line = Read-JsonLine -Process $process -TimeoutSeconds 5
            if ($null -eq $line) {
                throw 'Expected one JSONL record.'
            }
            [Console]::Out.WriteLine($line)
            if (-not $process.WaitForExit(5000)) {
                throw 'Child did not exit.'
            }
        }
        finally {
            $process.Dispose()
        }
        """;

    private sealed record ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
