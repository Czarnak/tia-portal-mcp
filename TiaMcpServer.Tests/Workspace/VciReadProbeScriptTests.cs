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
        Assert.DoesNotContain("workspace_read", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workspace_write", source, StringComparison.OrdinalIgnoreCase);
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
    public void GitProvenance_WorksWhenLastExitCodeIsInitiallyUnsetUnderStrictMode()
    {
        var command = """
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            Remove-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('REPLACE_HARNESS_PATH', [ref] $tokens, [ref] $errors)
            if ($errors.Count -ne 0) { throw 'Harness parsing failed.' }
            $functionAst = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-GitProvenance' }, $true)
            if ($null -eq $functionAst) { throw 'Get-GitProvenance was not found.' }
            Invoke-Expression $functionAst.Extent.Text
            Get-GitProvenance -RepositoryRoot 'REPLACE_REPOSITORY_ROOT' | ConvertTo-Json -Compress -Depth 10
            """
            .Replace("REPLACE_HARNESS_PATH", ScriptPath.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal)
            .Replace("REPLACE_REPOSITORY_ROOT", RepositoryRoot.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal);

        var result = RunPowerShell("-Command", command);

        Assert.True(result.ExitCode == 0, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.Matches("^[0-9a-f]{40}$", document.RootElement.GetProperty("commit").GetString());
        Assert.True(document.RootElement.GetProperty("trackedChangeCount").GetInt32() >= 0);
    }

    [Fact]
    public void Run_UsesOneFreshWorkerPerSessionAndCapturesAfterCanaryOutsideCasesJsonl()
    {
        var result = RunRunBlockOrderSmokeTest();

        Assert.True(result.ExitCode == 0, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("topLevelSessionLoopCount").GetInt32());
        Assert.True(root.GetProperty("freshWorkerInSession").GetBoolean());
        Assert.True(root.GetProperty("canaryBeforeAfterSnapshot").GetBoolean());
        Assert.True(root.GetProperty("afterSnapshotIsNotCasesJsonl").GetBoolean());
        Assert.True(root.GetProperty("cleanupInSessionFinally").GetBoolean());
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
    public void WorkerPayloadValidator_FailsClosedOnSchemaEnvelopeAndOutcomeDrift()
    {
        var result = RunWorkerPayloadValidatorSmokeTest();

        Assert.True(result.ExitCode == 0, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var outcomes = document.RootElement.EnumerateArray().Select(item => item.GetBoolean()).ToArray();
        Assert.Equal(
            new[]
            {
                true, true, true,
                false, false, false, false,
                false, false, false, false, false, false, false, false,
            },
            outcomes);
    }

    [Fact]
    public void WorkerPayloadValidator_EnforcesExclusiveCaseSpecificOutcomeBranchesAndOmittedNulls()
    {
        var result = RunWorkerPayloadOutcomeShapeSmokeTest();

        Assert.True(result.ExitCode == 0, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var outcomes = document.RootElement.EnumerateArray().Select(item => item.GetBoolean()).ToArray();
        Assert.Equal(
            new[]
            {
                true, true, true, true, true, true, true,
                false, false, false, false, false, false, false,
            },
            outcomes);
    }

    [Fact]
    public void CanaryGate_RequiresReturnedSnapshotAndReturnedNullCannotPassOverall()
    {
        var result = RunCanaryGateSmokeTest();

        Assert.True(result.ExitCode == 0, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var evidence = document.RootElement;
        Assert.True(evidence.GetProperty("returnedSnapshotUsable").GetBoolean());
        Assert.False(evidence.GetProperty("returnedNullUsable").GetBoolean());
        Assert.False(evidence.GetProperty("returnedWithoutSnapshotUsable").GetBoolean());
        Assert.False(evidence.GetProperty("returnedNullOverallPass").GetBoolean());
    }

    [Fact]
    public void WorkspaceRootDiscovery_FailsClosedWhenAnyDiscoveredRootIsMissing()
    {
        var result = RunWorkspaceRootDiscoverySmokeTest();

        Assert.True(result.ExitCode == 0, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.True(document.RootElement.GetProperty("completeRootsAccepted").GetBoolean());
        Assert.True(document.RootElement.GetProperty("nullRootRejected").GetBoolean());
        Assert.True(document.RootElement.GetProperty("blankRootRejected").GetBoolean());
    }

    [Fact]
    public void FilesystemSnapshot_NestedReparsePointMakesEvidenceAndOverallGateIncomplete()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vci-task8-reparse-{Guid.NewGuid():N}");
        var workspace = Path.Combine(root, "workspace");
        var target = Path.Combine(root, "target");
        var nestedLink = Path.Combine(workspace, "nested-link");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(workspace, "ordinary.txt"), "ordinary", Encoding.ASCII);
        File.WriteAllText(Path.Combine(target, "must-not-be-hashed.txt"), "outside", Encoding.ASCII);

        try
        {
            CreateDirectoryJunction(nestedLink, target);
            var result = RunFilesystemSnapshotSmokeTest(workspace);

            Assert.True(result.ExitCode == 0, result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var evidence = document.RootElement;
            Assert.False(evidence.GetProperty("snapshotComplete").GetBoolean());
            Assert.True(evidence.GetProperty("reparseOmissionObserved").GetBoolean());
            Assert.False(evidence.GetProperty("invariantComplete").GetBoolean());
            Assert.False(evidence.GetProperty("overallGate").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(nestedLink))
            {
                Directory.Delete(nestedLink);
            }
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TerminalCoverage_RequiresTheExactExpectedCaseInstanceIdSet()
    {
        var result = RunTerminalCoverageSmokeTest();

        Assert.True(result.ExitCode == 0, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var outcomes = document.RootElement.EnumerateArray().Select(item => item.GetBoolean()).ToArray();
        Assert.Equal(new[] { true, true, true, true }, outcomes);
    }

    [Fact]
    public void SnapshotAfterCoverage_RequiresExactPositiveReadCaseInstanceIds()
    {
        var result = RunSnapshotAfterCoverageSmokeTest();

        Assert.True(result.ExitCode == 0, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var outcomes = document.RootElement.EnumerateArray().Select(item => item.GetBoolean()).ToArray();
        Assert.Equal(new[] { true, true, true, true, true }, outcomes);
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
    public void EvidenceBundle_UsesAtomicUtf8DocumentsAndFlushesEveryJsonlRecord()
    {
        var source = ReadScript();

        Assert.Contains("function Write-AtomicJsonDocument", source, StringComparison.Ordinal);
        Assert.Contains("[Text.UTF8Encoding]::new($false)", source, StringComparison.Ordinal);
        Assert.Contains("[IO.File]::Move($temporaryPath, $Path, $true)", source, StringComparison.Ordinal);
        Assert.Contains("function Open-CasesWriter", source, StringComparison.Ordinal);
        Assert.Contains("function Write-CaseRecord", source, StringComparison.Ordinal);
        Assert.Contains("$Writer.Flush()", source, StringComparison.Ordinal);
        Assert.Contains("$Writer.BaseStream.Flush($true)", source, StringComparison.Ordinal);
        Assert.Contains("[IO.FileMode]::CreateNew", source, StringComparison.Ordinal);

        var root = Path.Combine(Path.GetTempPath(), $"vci-task8-writers-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var result = RunEvidenceWriterSmokeTest(root);
            Assert.True(result.ExitCode == 0, result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var evidence = document.RootElement;
            Assert.Equal(2, evidence.GetProperty("documentVersion").GetInt32());
            Assert.True(evidence.GetProperty("documentHasNoBom").GetBoolean());
            Assert.Equal(1, evidence.GetProperty("visibleLinesAfterFirstFlush").GetInt32());
            Assert.Equal(2, evidence.GetProperty("visibleLinesAfterSecondFlush").GetInt32());
            Assert.Equal(0, evidence.GetProperty("temporaryFileCount").GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TwoSessionRun_StartsFreshWorkersAndSendsOnlyTheLockedRequestEnvelope()
    {
        var source = ReadScript();

        Assert.Contains("$sessionIds = @('session-1', 'session-2')", source, StringComparison.Ordinal);
        Assert.Contains("function Invoke-ProbeSession", source, StringComparison.Ordinal);
        Assert.Contains(
            "$worker = Start-JsonLineProcess -Executable $WorkerExecutable -Arguments $WorkerArguments",
            source,
            StringComparison.Ordinal);
        Assert.Contains("Stop-JsonLineProcess -Process $worker", source, StringComparison.Ordinal);
        Assert.Contains("function New-ProbeWorkerRequest", source, StringComparison.Ordinal);
        Assert.Contains("method = 'probe_vci_read_contract'", source, StringComparison.Ordinal);
        Assert.Contains("projectPath = $ProjectPath", source, StringComparison.Ordinal);
        Assert.Contains("vciProbe = $Probe", source, StringComparison.Ordinal);
        Assert.DoesNotContain("confirm =", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("allowTiaConfirmations", source, StringComparison.OrdinalIgnoreCase);

        var result = RunRequestAndMatrixSmokeTest();
        Assert.True(result.ExitCode == 0, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var evidence = document.RootElement;
        Assert.Equal("method|projectPath|vciProbe", evidence.GetProperty("requestFields").GetString());
        Assert.True(evidence.GetProperty("stableAcrossSessions").GetBoolean());
        Assert.Equal("R-REP", evidence.GetProperty("penultimateCase").GetString());
        Assert.Equal("R-CANARY", evidence.GetProperty("lastCase").GetString());
        Assert.Equal(1, evidence.GetProperty("formatCaseCount").GetInt32());
        Assert.Equal(27, evidence.GetProperty("negativeCaseCount").GetInt32());
        Assert.Equal(9, evidence.GetProperty("formatNegativeCaseCount").GetInt32());
        Assert.Equal(8, evidence.GetProperty("groupNegativeCaseCount").GetInt32());
        Assert.Equal(8, evidence.GetProperty("workspaceNegativeCaseCount").GetInt32());
    }

    [Fact]
    public void CaseMatrix_ReconstructsNestedDuplicateGroupSelectorsAndExactInstanceIds()
    {
        var result = RunNestedGroupMatrixSmokeTest();

        Assert.True(result.ExitCode == 0, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var evidence = document.RootElement;

        var groupInventory = evidence.GetProperty("groupInventory").EnumerateArray().ToArray();
        Assert.Equal(
            new[] { "root", "root/0:0:Dup", "root/1:1:Dup", "root/1:1:Dup/0:0:Child" },
            groupInventory.Select(item => item.GetProperty("canonicalKey").GetString()));
        Assert.Equal(
            new[]
            {
                "[]",
                """[{"index":0,"name":"Dup","sameNameOrdinal":0}]""",
                """[{"index":1,"name":"Dup","sameNameOrdinal":1}]""",
                """[{"index":1,"name":"Dup","sameNameOrdinal":1},{"index":0,"name":"Child","sameNameOrdinal":0}]""",
            },
            groupInventory.Select(item => item.GetProperty("selector").GetProperty("groupPath").GetRawText()));

        var workspaceSelector = evidence.GetProperty("workspaceSelector");
        Assert.Equal("Nested", workspaceSelector.GetProperty("workspaceName").GetString());
        Assert.Equal("C:\\Nested", workspaceSelector.GetProperty("canonicalRootPath").GetString());
        Assert.Equal(
            """[{"index":1,"name":"Dup","sameNameOrdinal":1},{"index":0,"name":"Child","sameNameOrdinal":0}]""",
            workspaceSelector.GetProperty("groupPath").GetRawText());

        var groupNullCases = evidence.GetProperty("groupNullCases").EnumerateArray().ToArray();
        Assert.Equal(
            new[]
            {
                "n-grp-find-null-40c5c63bb864ce93170e",
                "n-grp-find-null-b5f9f168ce2d7ea43b52",
                "n-grp-find-null-ca2ce7320763a014d86d",
                "n-grp-find-null-7772b252cf1f8bf0bb31",
            },
            groupNullCases.Select(item => item.GetProperty("caseInstanceId").GetString()));
        Assert.Equal(
            groupInventory.Select(item => item.GetProperty("selector").GetProperty("groupPath").GetRawText()),
            groupNullCases.Select(item => item.GetProperty("groupPath").GetRawText()));

        var formatNullCase = evidence.GetProperty("formatNullCase");
        Assert.Equal("n-fmt-null-5cec1f3541b725768959", formatNullCase.GetProperty("caseInstanceId").GetString());
        Assert.Equal(
            """[{"index":1,"name":"Dup","sameNameOrdinal":1},{"index":0,"name":"Child","sameNameOrdinal":0}]""",
            formatNullCase.GetProperty("workspace").GetProperty("groupPath").GetRawText());
    }

    [Fact]
    public void CaseMatrix_EndsWithRepeatabilityAndCanaryAndFailsClosedOnEvidenceDrift()
    {
        var source = ReadScript();

        Assert.Contains("function New-CaseMatrix", source, StringComparison.Ordinal);
        Assert.Contains("$matrix.Add((New-CaseDefinition -CaseId 'R-REP'", source, StringComparison.Ordinal);
        Assert.Contains("$matrix.Add((New-CaseDefinition -CaseId 'R-CANARY'", source, StringComparison.Ordinal);
        Assert.Contains("function Assert-TerminalCoverage", source, StringComparison.Ordinal);
        Assert.Contains("duplicate_case_instance_id", source, StringComparison.Ordinal);
        Assert.Contains("missing_case_instance_id", source, StringComparison.Ordinal);
        Assert.Contains("schema_mismatch", source, StringComparison.Ordinal);
        Assert.Contains("malformed_worker_payload", source, StringComparison.Ordinal);
        Assert.Contains("filesystem_hashing_incomplete", source, StringComparison.Ordinal);
        Assert.Contains("project_state_changed", source, StringComparison.Ordinal);
        Assert.Contains("filesystem_changed", source, StringComparison.Ordinal);
        Assert.Contains("normalized_session_mismatch", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TransportFailures_AreTerminalTimeoutOrProcessLossRecordsWithoutVendorExceptions()
    {
        var source = ReadScript();

        Assert.Contains("function New-TransportFailureRecord", source, StringComparison.Ordinal);
        Assert.Contains("[ValidateSet('timed_out', 'process_lost')]", source, StringComparison.Ordinal);
        Assert.Contains("outcome = $Outcome", source, StringComparison.Ordinal);
        Assert.Contains("exception = $null", source, StringComparison.Ordinal);
        Assert.Contains("exitCode = $ExitCode", source, StringComparison.Ordinal);
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

    private static ScriptResult RunWorkerPayloadValidatorSmokeTest()
    {
        var command = """
            $harnessPath = 'REPLACE_HARNESS_PATH'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($harnessPath, [ref] $tokens, [ref] $errors)
            foreach ($functionName in @(
                    'ConvertFrom-JsonHashtable', 'Assert-JsonObjectShape', 'Assert-JsonArray',
                    'Assert-JsonString', 'Assert-JsonNullableString', 'Assert-JsonBoolean',
                    'Assert-JsonInteger', 'Assert-VciProbeException', 'Assert-VciProbeMember',
                    'Assert-VciProbeReturn', 'Assert-VciWorkspaceSelector',
                    'Assert-VciEngineeringObjectSelector', 'Assert-VciMappingSelector',
                    'Assert-VciProbeSnapshot', 'Assert-VciProbeRepeatability',
                    'Assert-VciProbeProjectState', 'Assert-VciProbeOmission',
                    'Assert-VciProbePayload', 'Test-WorkerPayload')) {
                $functionAst = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $functionName }, $true)
                if ($null -eq $functionAst) { throw "Function '$functionName' was not found." }
                Invoke-Expression $functionAst.Extent.Text
            }
            $validWorkerOutcomes = @('returned', 'returned_null', 'not_observable', 'threw')
            $request = [ordered]@{
                method = 'probe_vci_read_contract'
                projectPath = 'C:\P.ap21'
                vciProbe = [ordered]@{
                    runId = 'run'; sessionId = 'session-1'; caseId = 'R-SVC'; caseInstanceId = 'case-1'
                }
            }
            $basePayload = [ordered]@{
                schemaVersion = 'vci-read-probe/v1'
                runId = 'run'; sessionId = 'session-1'; caseId = 'R-SVC'; caseInstanceId = 'case-1'
                outcome = 'returned'
                snapshot = [ordered]@{
                    members = @([ordered]@{
                        name = 'Name'; clrTypeName = 'System.String'; stringValue = 'value'
                        isNull = $false
                    })
                    service = [ordered]@{
                        serviceAvailable = $true; rootGroupAvailable = $true; rootGroupCount = 1
                    }
                    groups = @([ordered]@{
                        enumerationIndex = 0; canonicalKey = 'root/0:0:G'; name = 'G'; depth = 1
                        childGroupCount = 0; workspaceCount = 1
                    })
                    workspaces = @([ordered]@{
                        enumerationIndex = 0; canonicalKey = 'root/0:0:G/workspace:0:W'; name = 'W'
                        rootPath = 'C:\W'; deleteUnusedTypeVersionFromLibrary = $false; mappedObjectCount = 1
                    })
                    mappings = @([ordered]@{
                        enumerationIndex = 0; canonicalKey = 'mapping:0'
                        selector = [ordered]@{
                            workspace = [ordered]@{
                                groupPath = @([ordered]@{ index = 0; name = 'G'; sameNameOrdinal = 0 })
                                workspaceName = 'W'; canonicalRootPath = 'C:\W'
                            }
                            engineeringObject = [ordered]@{
                                stableIdentifier = 'id'
                                structuralPath = @([ordered]@{ index = 0; name = 'Obj'; objectType = 'Block' })
                                fingerprint = 'fingerprint'
                            }
                            relativeDirectory = 'src'; fileName = 'Obj.xml'; format = 'xml'
                        }
                        status = 'Current'; statusProperty = 'Current'; getStatus = 'Current'; childStatus = 'Current'
                    })
                    candidates = @([ordered]@{
                        enumerationIndex = 0; canonicalKey = 'candidate:0'; description = 'XML'
                        runtimeTypeName = 'System.String'; isNull = $false
                    })
                    candidateCollectionRuntimeType = 'System.String[]'
                }
                projectState = [ordered]@{ isModifiedBefore = $false; isModifiedAfter = $false }
                omissions = @([ordered]@{
                    reason = 'budget'; budgetName = 'maxGroups'; budgetValue = 1; observedCount = 1
                    traversalPath = 'root'
                })
            }
            function New-Envelope([object] $Payload) {
                return [ordered]@{
                    success = $true
                    payload = ($Payload | ConvertTo-Json -Compress -Depth 100)
                    resolvedProjectPath = 'C:\P.ap21'
                } | ConvertTo-Json -Compress -Depth 100
            }
            function Test-Fixture([object] $Payload) {
                try {
                    $request.vciProbe.caseId = [string] $Payload.caseId
                    $null = Test-WorkerPayload -ResponseText (New-Envelope -Payload $Payload) -Request $request -ExpectedProjectPath 'C:\P.ap21'
                    return $true
                }
                catch {
                    return $false
                }
            }
            function Copy-Fixture([object] $Value) {
                return ($Value | ConvertTo-Json -Compress -Depth 100) | ConvertFrom-Json -AsHashtable -Depth 100
            }

            $wrongSchema = Copy-Fixture $basePayload; $wrongSchema.schemaVersion = 'wrong/v1'
            $unknownField = Copy-Fixture $basePayload; $unknownField.extra = 1
            $workerTimeout = Copy-Fixture $basePayload; $workerTimeout.outcome = 'timed_out'
            $missingOmissions = Copy-Fixture $basePayload; $missingOmissions.Remove('omissions')

            $repeatabilityObservation = [ordered]@{
                clrTypeName = 'System.String'; isNull = $false; stringValue = 'value'
                members = @([ordered]@{ name = 'Length'; clrTypeName = 'System.Int32'; stringValue = '5'; isNull = $false })
            }
            $validReturn = Copy-Fixture $basePayload; $validReturn.Remove('snapshot'); $validReturn.caseId = 'R-REP'
            $validReturn.repeatability = [ordered]@{ observations = @($repeatabilityObservation); isIdentical = $true }

            $validException = Copy-Fixture $basePayload; $validException.Remove('snapshot'); $validException.outcome = 'threw'
            $validException.exception = [ordered]@{
                exceptionTypeName = 'System.InvalidOperationException'; message = 'outer'; hResult = -1
                innerException = [ordered]@{
                    exceptionTypeName = 'System.Exception'; message = 'inner'; hResult = -2
                }
            }

            $malformedWorkspaceArray = Copy-Fixture $basePayload
            $malformedWorkspaceArray.snapshot.workspaces = 'not-an-array'

            $malformedGroup = Copy-Fixture $basePayload
            $malformedGroup.snapshot.groups = @([ordered]@{
                enumerationIndex = 'zero'; canonicalKey = 'root/0:0:G'; name = 'G'; depth = 1
                childGroupCount = 0; workspaceCount = 1
            })

            $outOfRangeInteger = Copy-Fixture $basePayload
            $outOfRangeInteger.snapshot.groups[0].enumerationIndex = 2147483648

            $malformedMapping = Copy-Fixture $basePayload
            $malformedMapping.snapshot.mappings = @([ordered]@{
                enumerationIndex = 0; canonicalKey = 'mapping:0'
                selector = [ordered]@{
                    workspace = [ordered]@{ groupPath = 'not-an-array'; workspaceName = 'W' }
                    engineeringObject = [ordered]@{ structuralPath = @() }
                }
            })

            $malformedReturn = Copy-Fixture $basePayload; $malformedReturn.Remove('snapshot'); $malformedReturn.caseId = 'N-FMT-NULL'
            $malformedReturn.return = Copy-Fixture $repeatabilityObservation
            $malformedReturn.return.members = 'not-an-array'

            $malformedException = Copy-Fixture $validException
            $malformedException.exception.innerException = 'not-an-object'

            $malformedRepeatability = Copy-Fixture $validReturn; $malformedRepeatability.repeatability = [ordered]@{
                observations = 'not-an-array'; isIdentical = $true
            }

            $malformedOmission = Copy-Fixture $basePayload; $malformedOmission.omissions = @([ordered]@{
                reason = 'budget'; budgetName = 'maxGroups'; budgetValue = 'one'; observedCount = 1
            })
            @(
                (Test-Fixture -Payload $basePayload),
                (Test-Fixture -Payload $validReturn),
                (Test-Fixture -Payload $validException),
                (Test-Fixture -Payload $wrongSchema),
                (Test-Fixture -Payload $unknownField),
                (Test-Fixture -Payload $workerTimeout),
                (Test-Fixture -Payload $missingOmissions),
                (Test-Fixture -Payload $malformedWorkspaceArray),
                (Test-Fixture -Payload $malformedGroup),
                (Test-Fixture -Payload $outOfRangeInteger),
                (Test-Fixture -Payload $malformedMapping),
                (Test-Fixture -Payload $malformedReturn),
                (Test-Fixture -Payload $malformedException),
                (Test-Fixture -Payload $malformedRepeatability),
                (Test-Fixture -Payload $malformedOmission)
            ) | ConvertTo-Json -Compress -Depth 10
            """.Replace("REPLACE_HARNESS_PATH", ScriptPath.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal);
        return RunPowerShell("-Command", command);
    }

    private static ScriptResult RunWorkerPayloadOutcomeShapeSmokeTest()
    {
        var command = """
            $harnessPath = 'REPLACE_HARNESS_PATH'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($harnessPath, [ref] $tokens, [ref] $errors)
            foreach ($functionName in @(
                    'Assert-JsonObjectShape', 'Assert-JsonArray', 'Assert-JsonString',
                    'Assert-JsonNullableString', 'Assert-JsonBoolean', 'Assert-JsonInteger',
                    'Assert-VciProbeException', 'Assert-VciProbeMember', 'Assert-VciProbeReturn',
                    'Assert-VciWorkspaceSelector', 'Assert-VciEngineeringObjectSelector',
                    'Assert-VciMappingSelector', 'Assert-VciProbeSnapshot',
                    'Assert-VciProbeRepeatability', 'Assert-VciProbeProjectState',
                    'Assert-VciProbeOmission', 'Assert-VciProbePayload')) {
                $functionAst = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $functionName }, $true)
                if ($null -eq $functionAst) { throw "Function '$functionName' was not found." }
                Invoke-Expression $functionAst.Extent.Text
            }

            function New-Base([string] $CaseId, [string] $Outcome) {
                return [ordered]@{
                    schemaVersion = 'vci-read-probe/v1'; runId = 'run'; sessionId = 'session-1'
                    caseId = $CaseId; caseInstanceId = 'instance'; outcome = $Outcome
                    projectState = [ordered]@{ isModifiedBefore = $false; isModifiedAfter = $false }
                    omissions = @()
                }
            }
            function New-Return([bool] $IsNull) {
                return [ordered]@{ clrTypeName = 'System.String'; isNull = $IsNull; members = @() }
            }
            function New-Snapshot {
                return [ordered]@{ members = @(); groups = @(); workspaces = @(); mappings = @(); candidates = @() }
            }
            function Copy-Fixture([object] $Value) {
                return ($Value | ConvertTo-Json -Compress -Depth 100) | ConvertFrom-Json -AsHashtable -Depth 100
            }
            function Test-Fixture([object] $Value) {
                try { Assert-VciProbePayload -Value $Value; return $true } catch { return $false }
            }

            $validSnapshot = New-Base 'R-SVC' 'returned'; $validSnapshot.snapshot = New-Snapshot
            $validFormatSnapshot = New-Base 'R-FMT' 'returned'; $validFormatSnapshot.snapshot = New-Snapshot
            $validNegative = New-Base 'N-GRP-FIND-EMPTY' 'returned'; $validNegative.return = New-Return -IsNull $false
            $validRepeatability = New-Base 'R-REP' 'returned'; $validRepeatability.repeatability = [ordered]@{
                observations = @((New-Return -IsNull $false), (New-Return -IsNull $false)); isIdentical = $true
            }
            $validReturnedNull = New-Base 'N-WS-FIND-MISSING' 'returned_null'; $validReturnedNull.return = New-Return -IsNull $true
            $validThrew = New-Base 'N-FMT-NULL' 'threw'; $validThrew.exception = [ordered]@{
                exceptionTypeName = 'System.ArgumentNullException'; message = 'value'; hResult = -1
            }
            $validNotObservable = New-Base 'N-MAP-INACCESSIBLE-FILE' 'not_observable'
            $validNotObservable.notObservableReason = 'no_inaccessible_mapping'

            $explicitNullReason = Copy-Fixture $validSnapshot; $explicitNullReason.notObservableReason = $null
            $contradictoryBranch = Copy-Fixture $validSnapshot; $contradictoryBranch.return = New-Return -IsNull $false
            $missingRequiredBranch = New-Base 'R-SVC' 'returned'
            $wrongReturnedNull = New-Base 'N-WS-FIND-MISSING' 'returned_null'; $wrongReturnedNull.return = New-Return -IsNull $false
            $notObservableWithReturn = Copy-Fixture $validNotObservable; $notObservableWithReturn.return = New-Return -IsNull $false
            $canaryReturnedNull = New-Base 'R-CANARY' 'returned_null'; $canaryReturnedNull.return = New-Return -IsNull $true
            $serviceReturnedNull = New-Base 'R-SVC' 'returned_null'; $serviceReturnedNull.return = New-Return -IsNull $true

            @(
                (Test-Fixture $validSnapshot),
                (Test-Fixture $validFormatSnapshot),
                (Test-Fixture $validNegative),
                (Test-Fixture $validRepeatability),
                (Test-Fixture $validReturnedNull),
                (Test-Fixture $validThrew),
                (Test-Fixture $validNotObservable),
                (Test-Fixture $explicitNullReason),
                (Test-Fixture $contradictoryBranch),
                (Test-Fixture $missingRequiredBranch),
                (Test-Fixture $wrongReturnedNull),
                (Test-Fixture $notObservableWithReturn),
                (Test-Fixture $canaryReturnedNull),
                (Test-Fixture $serviceReturnedNull)
            ) | ConvertTo-Json -Compress -Depth 10
            """.Replace("REPLACE_HARNESS_PATH", ScriptPath.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal);
        return RunPowerShell("-Command", command);
    }

    private static ScriptResult RunCanaryGateSmokeTest()
    {
        var command = """
            $harnessPath = 'REPLACE_HARNESS_PATH'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($harnessPath, [ref] $tokens, [ref] $errors)
            $functionAst = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-CanaryUsable' }, $true)
            if ($null -eq $functionAst) { throw 'Test-CanaryUsable was not found.' }
            Invoke-Expression $functionAst.Extent.Text

            function New-Canary([string] $Outcome, [object] $Payload) {
                return [ordered]@{
                    sessionId = 'session-1'; caseId = 'R-CANARY'; outcome = $Outcome
                    workerPayload = $Payload
                }
            }
            $returnedSnapshot = New-Canary 'returned' ([ordered]@{ snapshot = [ordered]@{} })
            $returnedNull = New-Canary 'returned_null' ([ordered]@{
                return = [ordered]@{ clrTypeName = 'null'; isNull = $true; members = @() }
            })
            $returnedWithoutSnapshot = New-Canary 'returned' ([ordered]@{})

            $returnedNullUsable = Test-CanaryUsable -Records @($returnedNull) -SessionId 'session-1'
            $failureReasons = [Collections.Generic.List[string]]::new()
            if (-not $returnedNullUsable) { $failureReasons.Add('session-1 canary_not_usable') }
            [ordered]@{
                returnedSnapshotUsable = Test-CanaryUsable -Records @($returnedSnapshot) -SessionId 'session-1'
                returnedNullUsable = $returnedNullUsable
                returnedWithoutSnapshotUsable = Test-CanaryUsable -Records @($returnedWithoutSnapshot) -SessionId 'session-1'
                returnedNullOverallPass = $failureReasons.Count -eq 0
            } | ConvertTo-Json -Compress -Depth 10
            """.Replace("REPLACE_HARNESS_PATH", ScriptPath.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal);
        return RunPowerShell("-Command", command);
    }

    private static ScriptResult RunWorkspaceRootDiscoverySmokeTest()
    {
        var command = """
            $harnessPath = 'REPLACE_HARNESS_PATH'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($harnessPath, [ref] $tokens, [ref] $errors)
            $functionAst = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-WorkspaceRoots' }, $true)
            if ($null -eq $functionAst) { throw 'Get-WorkspaceRoots was not found.' }
            Invoke-Expression $functionAst.Extent.Text

            function New-Record([object[]] $Workspaces) {
                return [ordered]@{
                    caseId = 'R-WS'
                    workerPayload = [ordered]@{ snapshot = [ordered]@{ workspaces = $Workspaces } }
                }
            }
            function Test-Rejected([object] $RootPath) {
                try {
                    $null = Get-WorkspaceRoots -SnapshotRecords @((New-Record -Workspaces @(
                        [ordered]@{ canonicalKey = 'root/workspace:0:Complete'; name = 'Complete'; rootPath = 'C:\Complete' },
                        [ordered]@{ canonicalKey = 'root/workspace:1:Incomplete'; name = 'Incomplete'; rootPath = $RootPath }
                    )))
                    return $false
                }
                catch {
                    return $_.Exception.Message.StartsWith('filesystem_hashing_incomplete:', [StringComparison]::Ordinal)
                }
            }

            [ordered]@{
                completeRootsAccepted = @(Get-WorkspaceRoots -SnapshotRecords @((New-Record -Workspaces @(
                    [ordered]@{ canonicalKey = 'root/workspace:0:A'; name = 'A'; rootPath = 'C:\A' },
                    [ordered]@{ canonicalKey = 'root/workspace:1:B'; name = 'B'; rootPath = 'C:\B' }
                )))).Count -eq 2
                nullRootRejected = Test-Rejected -RootPath $null
                blankRootRejected = Test-Rejected -RootPath '   '
            } | ConvertTo-Json -Compress -Depth 10
            """.Replace("REPLACE_HARNESS_PATH", ScriptPath.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal);
        return RunPowerShell("-Command", command);
    }

    private static ScriptResult RunFilesystemSnapshotSmokeTest(string workspaceRoot)
    {
        var command = """
            $harnessPath = 'REPLACE_HARNESS_PATH'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($harnessPath, [ref] $tokens, [ref] $errors)
            foreach ($functionName in @('Get-UtcTimestamp', 'Get-FilesystemSnapshot', 'Compare-FilesystemSnapshots')) {
                $functionAst = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $functionName }, $true)
                if ($null -eq $functionAst) { throw "Function '$functionName' was not found." }
                Invoke-Expression $functionAst.Extent.Text
            }

            $snapshot = Get-FilesystemSnapshot -WorkspaceRoots @('REPLACE_WORKSPACE_ROOT') -MaxFiles 100 -MaxBytes 1048576
            $invariant = Compare-FilesystemSnapshots -Before $snapshot -After $snapshot
            [ordered]@{
                snapshotComplete = $snapshot.complete
                reparseOmissionObserved = @($snapshot.omissions | Where-Object { $_.reason -eq 'reparse_point_not_followed' }).Count -eq 1
                invariantComplete = $invariant.complete
                overallGate = $snapshot.complete -and $invariant.complete -and $invariant.unchanged
            } | ConvertTo-Json -Compress -Depth 10
            """
            .Replace("REPLACE_HARNESS_PATH", ScriptPath.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal)
            .Replace("REPLACE_WORKSPACE_ROOT", workspaceRoot.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal);
        return RunPowerShell("-Command", command);
    }

    private static void CreateDirectoryJunction(string junctionPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(junctionPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the junction fixture process.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"Failed to create test junction.{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{standardOutput}{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{standardError}");
    }

    private static ScriptResult RunTerminalCoverageSmokeTest()
    {
        var command = """
            $harnessPath = 'REPLACE_HARNESS_PATH'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($harnessPath, [ref] $tokens, [ref] $errors)
            $functionAst = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-TerminalCoverage' }, $true)
            if ($null -eq $functionAst) { throw 'Assert-TerminalCoverage was not found.' }
            Invoke-Expression $functionAst.Extent.Text

            function New-Record([string] $Id) {
                return [ordered]@{
                    schemaVersion = 'vci-phase1-read-case-evidence/v1'; terminal = $true
                    sessionId = 'session-1'; caseId = $Id; caseInstanceId = $Id
                }
            }
            function Test-Coverage([object[]] $Records, [AllowNull()] [string] $ExpectedError) {
                try {
                    Assert-TerminalCoverage -Records $Records -SessionId 'session-1' -ExpectedCaseInstanceIds @('A', 'B')
                    return [string]::IsNullOrEmpty($ExpectedError)
                }
                catch {
                    return -not [string]::IsNullOrEmpty($ExpectedError) -and $_.Exception.Message.StartsWith($ExpectedError, [StringComparison]::Ordinal)
                }
            }
            @(
                (Test-Coverage -Records @((New-Record 'A'), (New-Record 'B')) -ExpectedError $null),
                (Test-Coverage -Records @((New-Record 'A')) -ExpectedError 'missing_case_instance_id:'),
                (Test-Coverage -Records @((New-Record 'A'), (New-Record 'A'), (New-Record 'B')) -ExpectedError 'duplicate_case_instance_id:'),
                (Test-Coverage -Records @((New-Record 'A'), (New-Record 'B'), (New-Record 'C')) -ExpectedError 'unexpected_case_instance_id:')
            ) | ConvertTo-Json -Compress -Depth 10
            """.Replace("REPLACE_HARNESS_PATH", ScriptPath.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal);
        return RunPowerShell("-Command", command);
    }

    private static ScriptResult RunSnapshotAfterCoverageSmokeTest()
    {
        var command = """
            $harnessPath = 'REPLACE_HARNESS_PATH'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($harnessPath, [ref] $tokens, [ref] $errors)
            $functionAst = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-SnapshotAfterCoverage' }, $true)
            if ($null -eq $functionAst) { throw 'Assert-SnapshotAfterCoverage was not found.' }
            Invoke-Expression $functionAst.Extent.Text

            $expectedIds = @('r-svc-after', 'r-grp-after', 'r-ws-after', 'r-map-after')
            function New-Record([string] $CaseId, [string] $InstanceId) {
                return [ordered]@{
                    schemaVersion = 'vci-phase1-read-case-evidence/v1'; terminal = $true
                    sessionId = 'session-1'; caseId = $CaseId; caseInstanceId = $InstanceId
                }
            }
            $valid = @(
                (New-Record 'R-SVC' 'r-svc-after'),
                (New-Record 'R-GRP' 'r-grp-after'),
                (New-Record 'R-WS' 'r-ws-after'),
                (New-Record 'R-MAP' 'r-map-after')
            )
            function Test-Coverage([object[]] $Records, [AllowNull()] [string] $ExpectedError) {
                try {
                    Assert-SnapshotAfterCoverage -Records $Records -SessionId 'session-1' -ExpectedCaseInstanceIds $expectedIds
                    return [string]::IsNullOrEmpty($ExpectedError)
                }
                catch {
                    return -not [string]::IsNullOrEmpty($ExpectedError) -and $_.Exception.Message.StartsWith($ExpectedError, [StringComparison]::Ordinal)
                }
            }
            @(
                (Test-Coverage -Records $valid -ExpectedError $null),
                (Test-Coverage -Records @($valid | Where-Object { $_.caseId -ne 'R-MAP' }) -ExpectedError 'missing_snapshot_after_case_instance_id:'),
                (Test-Coverage -Records @($valid + (New-Record 'R-SVC' 'r-svc-after')) -ExpectedError 'duplicate_snapshot_after_case_instance_id:'),
                (Test-Coverage -Records @($valid + (New-Record 'R-SVC' 'unexpected-after')) -ExpectedError 'unexpected_snapshot_after_case_instance_id:'),
                (Test-Coverage -Records @($valid[1], $valid[0], $valid[2], $valid[3]) -ExpectedError 'unexpected_snapshot_after_case_instance_id:')
            ) | ConvertTo-Json -Compress -Depth 10
            """.Replace("REPLACE_HARNESS_PATH", ScriptPath.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal);
        return RunPowerShell("-Command", command);
    }

    private static ScriptResult RunEvidenceWriterSmokeTest(string root)
    {
        var command = """
            $harnessPath = 'REPLACE_HARNESS_PATH'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($harnessPath, [ref] $tokens, [ref] $errors)
            if ($errors.Count -ne 0) { throw 'Harness parsing failed.' }
            foreach ($functionName in @('Write-AtomicJsonDocument', 'Open-CasesWriter', 'Write-CaseRecord')) {
                $functionAst = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $functionName }, $true)
                if ($null -eq $functionAst) { throw "Function '$functionName' was not found." }
                Invoke-Expression $functionAst.Extent.Text
            }

            $root = 'REPLACE_ROOT'
            $documentPath = Join-Path $root 'document.json'
            $casesPath = Join-Path $root 'cases.jsonl'
            Write-AtomicJsonDocument -Path $documentPath -Value ([ordered]@{ version = 1 })
            Write-AtomicJsonDocument -Path $documentPath -Value ([ordered]@{ version = 2 })
            $documentBytes = [IO.File]::ReadAllBytes($documentPath)
            $documentValue = [IO.File]::ReadAllText($documentPath) | ConvertFrom-Json

            function Get-VisibleLineCount {
                param([string] $Path)

                $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
                $reader = [IO.StreamReader]::new($stream, [Text.UTF8Encoding]::new($false))
                try {
                    return @($reader.ReadToEnd().Split([Environment]::NewLine, [StringSplitOptions]::RemoveEmptyEntries)).Count
                }
                finally {
                    $reader.Dispose()
                }
            }

            $writer = Open-CasesWriter -Path $casesPath
            try {
                Write-CaseRecord -Writer $writer -Record ([ordered]@{ sequence = 1 })
                $firstCount = Get-VisibleLineCount -Path $casesPath
                Write-CaseRecord -Writer $writer -Record ([ordered]@{ sequence = 2 })
                $secondCount = Get-VisibleLineCount -Path $casesPath
            }
            finally {
                $writer.Dispose()
            }

            [ordered]@{
                documentVersion = $documentValue.version
                documentHasNoBom = -not ($documentBytes.Length -ge 3 -and $documentBytes[0] -eq 0xEF -and $documentBytes[1] -eq 0xBB -and $documentBytes[2] -eq 0xBF)
                visibleLinesAfterFirstFlush = $firstCount
                visibleLinesAfterSecondFlush = $secondCount
                temporaryFileCount = @(Get-ChildItem -LiteralPath $root -Filter '*.tmp-*' -File).Count
            } | ConvertTo-Json -Compress -Depth 10
            """
            .Replace("REPLACE_HARNESS_PATH", ScriptPath.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal)
            .Replace("REPLACE_ROOT", root.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal);
        return RunPowerShell("-Command", command);
    }

    private static ScriptResult RunRequestAndMatrixSmokeTest()
    {
        var command = """
            $harnessPath = 'REPLACE_HARNESS_PATH'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($harnessPath, [ref] $tokens, [ref] $errors)
            if ($errors.Count -ne 0) { throw 'Harness parsing failed.' }
            foreach ($functionName in @(
                    'ConvertTo-CanonicalValue', 'ConvertTo-CanonicalJson', 'Get-Sha256Text',
                    'New-CaseDefinition', 'Get-CaseInstanceId', 'New-ProbeWorkerRequest',
                    'Get-FormatPairs', 'Get-GroupPathInventory', 'Get-WorkspaceInventory',
                    'New-CaseMatrix')) {
                $functionAst = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $functionName }, $true)
                if ($null -eq $functionAst) { throw "Function '$functionName' was not found." }
                Invoke-Expression $functionAst.Extent.Text
            }

            $probeBudgets = [ordered]@{
                maxGroupDepth = 16; maxGroups = 500; maxWorkspaces = 500; maxMappings = 5000
                maxEngineeringObjects = 200; maxCollectionItems = 5000
            }
            $negativeCaseIds = @(
                'N-FMT-FOREIGN', 'N-FMT-NULL', 'N-FMT-UNSUPPORTED',
                'N-GRP-FIND-EMPTY', 'N-GRP-FIND-MISSING', 'N-GRP-FIND-NULL',
                'N-GRP-FIND-WHITESPACE', 'N-MAP-INACCESSIBLE-FILE',
                'N-MAP-MISSING-FILE', 'N-WS-FIND-EMPTY', 'N-WS-FIND-MISSING',
                'N-WS-FIND-NULL', 'N-WS-FIND-WHITESPACE'
            )
            $workspace = [ordered]@{ groupPath = @(); workspaceName = 'W'; canonicalRootPath = 'C:\W' }
            $engineeringObject = [ordered]@{ stableIdentifier = 'id'; structuralPath = @(); fingerprint = 'fingerprint' }
            $mapping = [ordered]@{ selector = [ordered]@{ workspace = $workspace; engineeringObject = $engineeringObject } }
            $groups = @([ordered]@{
                enumerationIndex = 0; canonicalKey = 'root/0:0:G'; name = 'G'; depth = 1
                parentCanonicalKey = $null; childGroupCount = 0; workspaceCount = 1
            })
            $workspaces = @(
                [ordered]@{ enumerationIndex = 0; canonicalKey = 'root/workspace:0:W'; name = 'W'; rootPath = 'C:\W' },
                [ordered]@{ enumerationIndex = 1; canonicalKey = 'root/workspace:1:NoMapRoot'; name = 'NoMapRoot'; rootPath = 'C:\NoMapRoot' },
                [ordered]@{ enumerationIndex = 2; canonicalKey = 'root/0:0:G/workspace:0:NoMapGroup'; name = 'NoMapGroup'; rootPath = 'C:\NoMapGroup' }
            )
            $matrix = @(New-CaseMatrix -Mappings @($mapping, $mapping) -GroupSnapshots $groups -WorkspaceSnapshots $workspaces -SecondaryProjectPath $null)
            $definition = $matrix[0]
            $request1 = New-ProbeWorkerRequest -RunId 'run' -SessionId 'session-1' -ProjectPath 'C:\P.ap21' -Definition $definition
            $request2 = New-ProbeWorkerRequest -RunId 'run' -SessionId 'session-2' -ProjectPath 'C:\P.ap21' -Definition $definition

            [ordered]@{
                requestFields = (@($request1.Keys | Sort-Object) -join '|')
                stableAcrossSessions = $request1.vciProbe.caseInstanceId -ceq $request2.vciProbe.caseInstanceId
                penultimateCase = $matrix[$matrix.Count - 2].caseId
                lastCase = $matrix[$matrix.Count - 1].caseId
                formatCaseCount = @($matrix | Where-Object { $_.caseId -eq 'R-FMT' }).Count
                negativeCaseCount = @($matrix | Where-Object { $_.caseId.StartsWith('N-', [StringComparison]::Ordinal) }).Count
                formatNegativeCaseCount = @($matrix | Where-Object { $_.caseId.StartsWith('N-FMT-', [StringComparison]::Ordinal) }).Count
                groupNegativeCaseCount = @($matrix | Where-Object { $_.caseId.StartsWith('N-GRP-', [StringComparison]::Ordinal) }).Count
                workspaceNegativeCaseCount = @($matrix | Where-Object { $_.caseId.StartsWith('N-WS-', [StringComparison]::Ordinal) }).Count
            } | ConvertTo-Json -Compress -Depth 10
            """.Replace("REPLACE_HARNESS_PATH", ScriptPath.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal);
        return RunPowerShell("-Command", command);
    }

    private static ScriptResult RunNestedGroupMatrixSmokeTest()
    {
        var harnessPath = ScriptPath;
        string? mutatedHarnessPath = null;
        if (string.Equals(
                Environment.GetEnvironmentVariable("VCI_TASK8_MUTATE_GROUP_ORDINAL"),
                "1",
                StringComparison.Ordinal))
        {
            const string marker = "        $groupPath = @($pathsByKey[$parentKey]) + ,([ordered]@{";
            var source = ReadScript();
            var mutatedSource = source.Replace(
                marker,
                "        $sameNameOrdinal = 0" + Environment.NewLine + marker,
                StringComparison.Ordinal);
            Assert.NotEqual(source, mutatedSource);
            mutatedHarnessPath = Path.Combine(
                Path.GetTempPath(),
                $"vci-task8-group-inventory-mutant-{Guid.NewGuid():N}.ps1");
            File.WriteAllText(mutatedHarnessPath, mutatedSource, Encoding.ASCII);
            harnessPath = mutatedHarnessPath;
        }

        try
        {
            var command = """
                $harnessPath = 'REPLACE_HARNESS_PATH'
                $tokens = $null; $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($harnessPath, [ref] $tokens, [ref] $errors)
                if ($errors.Count -ne 0) { throw 'Harness parsing failed.' }
                foreach ($functionName in @(
                        'ConvertTo-CanonicalValue', 'ConvertTo-CanonicalJson', 'Get-Sha256Text',
                        'New-CaseDefinition', 'Get-CaseInstanceId', 'Get-FormatPairs',
                        'Get-GroupPathInventory', 'Get-WorkspaceInventory', 'New-CaseMatrix')) {
                    $functionAst = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $functionName }, $true)
                    if ($null -eq $functionAst) { throw "Function '$functionName' was not found." }
                    Invoke-Expression $functionAst.Extent.Text
                }

                $probeBudgets = [ordered]@{
                    maxGroupDepth = 16; maxGroups = 500; maxWorkspaces = 500; maxMappings = 5000
                    maxEngineeringObjects = 200; maxCollectionItems = 5000
                }
                $negativeCaseIds = @(
                    'N-FMT-FOREIGN', 'N-FMT-NULL', 'N-FMT-UNSUPPORTED',
                    'N-GRP-FIND-EMPTY', 'N-GRP-FIND-MISSING', 'N-GRP-FIND-NULL',
                    'N-GRP-FIND-WHITESPACE', 'N-MAP-INACCESSIBLE-FILE',
                    'N-MAP-MISSING-FILE', 'N-WS-FIND-EMPTY', 'N-WS-FIND-MISSING',
                    'N-WS-FIND-NULL', 'N-WS-FIND-WHITESPACE'
                )
                $groups = @(
                    [ordered]@{
                        enumerationIndex = 0; canonicalKey = 'root/0:0:Dup'; name = 'Dup'; depth = 1
                        parentCanonicalKey = $null; childGroupCount = 0; workspaceCount = 0
                    },
                    [ordered]@{
                        enumerationIndex = 1; canonicalKey = 'root/1:1:Dup'; name = 'Dup'; depth = 1
                        parentCanonicalKey = $null; childGroupCount = 1; workspaceCount = 0
                    },
                    [ordered]@{
                        enumerationIndex = 2; canonicalKey = 'root/1:1:Dup/0:0:Child'; name = 'Child'; depth = 2
                        parentCanonicalKey = 'root/1:1:Dup'; childGroupCount = 0; workspaceCount = 1
                    }
                )
                $workspaces = @([ordered]@{
                    enumerationIndex = 0
                    canonicalKey = 'root/1:1:Dup/0:0:Child/workspace:0:Nested'
                    name = 'Nested'; rootPath = 'C:\Nested'
                })

                $groupInventory = @(Get-GroupPathInventory -GroupSnapshots $groups)
                $workspaceInventory = @(Get-WorkspaceInventory -WorkspaceSnapshots $workspaces -GroupPathInventory $groupInventory)
                $matrix = @(New-CaseMatrix -Mappings @() -GroupSnapshots $groups -WorkspaceSnapshots $workspaces -SecondaryProjectPath $null)
                $groupNullCases = @($matrix | Where-Object { $_.caseId -eq 'N-GRP-FIND-NULL' } | ForEach-Object {
                    [ordered]@{
                        caseInstanceId = Get-CaseInstanceId -Definition $_
                        groupPath = $_.workspace.groupPath
                    }
                })
                $formatNullCases = @($matrix | Where-Object { $_.caseId -eq 'N-FMT-NULL' })
                if ($formatNullCases.Count -ne 1) { throw 'Expected one nested N-FMT-NULL definition.' }

                [ordered]@{
                    groupInventory = $groupInventory
                    workspaceSelector = $workspaceInventory[0].selector
                    groupNullCases = $groupNullCases
                    formatNullCase = [ordered]@{
                        caseInstanceId = Get-CaseInstanceId -Definition $formatNullCases[0]
                        workspace = $formatNullCases[0].workspace
                    }
                } | ConvertTo-Json -Compress -Depth 100
                """.Replace(
                    "REPLACE_HARNESS_PATH",
                    harnessPath.Replace("'", "''", StringComparison.Ordinal),
                    StringComparison.Ordinal);
            return RunPowerShell("-Command", command);
        }
        finally
        {
            if (mutatedHarnessPath is not null)
            {
                File.Delete(mutatedHarnessPath);
            }
        }
    }

    private static ScriptResult RunRunBlockOrderSmokeTest()
    {
        var command = """
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('REPLACE_HARNESS_PATH', [ref] $tokens, [ref] $errors)
            if ($errors.Count -ne 0) { throw 'Harness parsing failed.' }
            $sessionLoops = @($ast.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.ForEachStatementAst] -and
                        $node.Extent.Text.Contains('foreach ($sessionId in $sessionIds)', [StringComparison]::Ordinal) -and
                        $node.Extent.Text.Contains('Invoke-ProbeSession', [StringComparison]::Ordinal)
                    }, $true))

            $sessionFunction = $ast.Find({
                    param($node)
                    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                    $node.Name -eq 'Invoke-ProbeSession'
                }, $true)
            if ($null -eq $sessionFunction) { throw 'Invoke-ProbeSession was not found.' }
            $functionText = $sessionFunction.Extent.Text
            $startIndex = $functionText.IndexOf(
                '$worker = Start-JsonLineProcess -Executable $WorkerExecutable -Arguments $WorkerArguments',
                [StringComparison]::Ordinal)
            $canaryIndex = $functionText.IndexOf(
                "caseId -ne 'R-CANARY'",
                [StringComparison]::Ordinal)
            $afterIndex = $functionText.IndexOf(
                "New-SnapshotDefinitions -Phase 'after-canary'",
                [StringComparison]::Ordinal)
            $recordFalseIndex = $functionText.IndexOf(
                '-RecordCase $false',
                [StringComparison]::Ordinal)
            $cleanupIndex = $functionText.IndexOf(
                'Stop-JsonLineProcess -Process $worker',
                [StringComparison]::Ordinal)

            [ordered]@{
                topLevelSessionLoopCount = $sessionLoops.Count
                freshWorkerInSession = $startIndex -ge 0
                canaryBeforeAfterSnapshot = $canaryIndex -ge 0 -and $canaryIndex -lt $afterIndex
                afterSnapshotIsNotCasesJsonl = $recordFalseIndex -gt $afterIndex
                cleanupInSessionFinally = $cleanupIndex -gt $afterIndex
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
