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
    public void TerminalCoverage_RequiresTheExactExpectedCaseInstanceIdSet()
    {
        var result = RunTerminalCoverageSmokeTest();

        Assert.True(result.ExitCode == 0, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var outcomes = document.RootElement.EnumerateArray().Select(item => item.GetBoolean()).ToArray();
        Assert.Equal(new[] { true, true, true, true }, outcomes);
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

            $validReturn = Copy-Fixture $basePayload; $validReturn.Remove('snapshot')
            $validReturn.return = [ordered]@{
                clrTypeName = 'System.String'; isNull = $false; stringValue = 'value'
                members = @([ordered]@{ name = 'Length'; clrTypeName = 'System.Int32'; stringValue = '5'; isNull = $false })
            }
            $validReturn.repeatability = [ordered]@{ observations = @($validReturn.return); isIdentical = $true }

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

            $malformedReturn = Copy-Fixture $validReturn
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
