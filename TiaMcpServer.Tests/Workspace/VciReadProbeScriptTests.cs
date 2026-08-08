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
    public void Run_UsesTheBoundedJsonLineReaderForVendorFreeTransportPreflight()
    {
        var source = ReadScript();

        Assert.Contains("__task7_transport_probe__", source, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"StandardInput\.WriteLine\(\$transportProbe\)", RegexOptions.CultureInvariant),
            source);
        Assert.Matches(
            new Regex(
                @"Read-JsonLine\s+-Process\s+\$worker\s+-TimeoutSeconds\s+\$TimeoutSeconds",
                RegexOptions.CultureInvariant),
            source);
    }

    [Fact]
    public void EvidenceRootPreflight_RejectsExistingFiles()
    {
        var source = ReadScript();

        Assert.Contains("if (-not $item.PSIsContainer)", source, StringComparison.Ordinal);
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
