using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            "archive_project",
            "close_project",
            "compile_check",
            "create_project",
            "network_write",
            "open_project",
            "plc_write",
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
    public void BlockHeaderAcceptance_ValidatesRequiredMatrixAcrossAllPages()
    {
        var result = RunHarnessFunctions(
            ["Assert-Condition", "Assert-BlockHeaderExpectationDocument", "Get-SnapshotNodes", "Resolve-NodeBySelector", "Assert-BlockHeaderExpectations"],
            """
            function Node($id, $parent, $type, $name, $details) {
                [pscustomobject]@{
                    nodeId = $id
                    parentNodeId = $parent
                    nodeType = $type
                    name = $name
                    details = $details
                }
            }
            function Segment($type, $name) { [pscustomobject]@{ nodeType = $type; name = $name } }
            function Case($id, $kind, $selector, $name, $details, $absent) {
                [pscustomobject]@{
                    id = $id
                    kind = $kind
                    selector = $selector
                    expected = [pscustomobject]@{
                        name = $name
                        details = $details
                        absentDetails = $absent
                    }
                }
            }

            $pages = @(
                [pscustomobject]@{ result = [pscustomobject]@{ nodes = @(
                    (Node 'device' $null 'Device' 'PLC_1' ([pscustomobject]@{})),
                    (Node 'software' 'device' 'PlcSoftware' 'Program' ([pscustomobject]@{})),
                    (Node 'blocks' 'software' 'BlockFolder' 'Program blocks' ([pscustomobject]@{}))
                ) } },
                [pscustomobject]@{ result = [pscustomobject]@{ nodes = @(
                    (Node 'user' 'blocks' 'FB' 'UserEngineering' ([pscustomobject]@{
                        HeaderAuthor = 'Ada Lovelace'
                        HeaderVersion = '1.2'
                        HeaderFamily = 'Motion'
                        HeaderName = 'User Header'
                    })),
                    (Node 'system-folder' 'software' 'SystemBlockFolder' 'System blocks' ([pscustomobject]@{})),
                    (Node 'system' 'system-folder' 'FC' 'SystemFunction' ([pscustomobject]@{
                        IsSystemBlock = 'true'
                        HeaderName = 'System Header'
                    }))
                ) } },
                [pscustomobject]@{ result = [pscustomobject]@{ nodes = @(
                    (Node 'unit' 'software' 'SoftwareUnit' 'UnitA' ([pscustomobject]@{})),
                    (Node 'unit-blocks' 'unit' 'BlockFolder' 'Program blocks' ([pscustomobject]@{})),
                    (Node 'unit-block' 'unit-blocks' 'FB' 'UnitFunction' ([pscustomobject]@{
                        SoftwareUnit = 'UnitA'
                        HeaderAuthor = 'Unit Author'
                    })),
                    (Node 'blank' 'blocks' 'FC' 'BlankHeaders' ([pscustomobject]@{
                        Number = '7'
                        ProgrammingLanguage = 'SCL'
                    }))
                ) } }
            )

            $device = Segment 'Device' 'PLC_1'
            $software = Segment 'PlcSoftware' 'Program'
            $blocks = Segment 'BlockFolder' 'Program blocks'
            $expectations = [pscustomobject]@{
                schemaVersion = 'issue-30-block-header-expectations/v1'
                cases = @(
                    (Case 'user-all-fields' 'user' @($device, $software, $blocks, (Segment 'FB' 'UserEngineering')) 'UserEngineering' ([pscustomobject]@{
                        HeaderAuthor = 'Ada Lovelace'
                        HeaderVersion = '1.2'
                        HeaderFamily = 'Motion'
                        HeaderName = 'User Header'
                    }) @()),
                    (Case 'system-block' 'system' @($device, $software, (Segment 'SystemBlockFolder' 'System blocks'), (Segment 'FC' 'SystemFunction')) 'SystemFunction' ([pscustomobject]@{
                        HeaderName = 'System Header'
                    }) @('HeaderFamily')),
                    (Case 'software-unit-block' 'softwareUnit' @($device, $software, (Segment 'SoftwareUnit' 'UnitA'), $blocks, (Segment 'FB' 'UnitFunction')) 'UnitFunction' ([pscustomobject]@{
                        HeaderAuthor = 'Unit Author'
                    }) @()),
                    (Case 'blank-headers' 'blank' @($device, $software, $blocks, (Segment 'FC' 'BlankHeaders')) 'BlankHeaders' ([pscustomobject]@{}) @(
                        'HeaderAuthor', 'HeaderVersion', 'HeaderFamily', 'HeaderName'
                    ))
                )
            }

            $evidence = Assert-BlockHeaderExpectations -Pages $pages -Expectations $expectations
            if ($evidence.cases.Count -ne 4) { throw 'Did not retain all four acceptance cases.' }
            if (-not $evidence.allHeaderFieldsVerified) { throw 'Did not verify all four header fields.' }
            if (-not $evidence.engineeringNameDiffersFromHeaderName) { throw 'Did not verify independent engineering and header names.' }
            if (-not $evidence.blankHeadersOmitted) { throw 'Did not verify blank header omission.' }
            $systemEvidence = @($evidence.cases | Where-Object { $_.id -ceq 'system-block' })[0]
            $systemEvidenceJson = $systemEvidence | ConvertTo-Json -Compress -Depth 10
            if ($systemEvidenceJson -cnotmatch '"absentDetails":\["HeaderFamily"\]') {
                throw "One-item absentDetails was not retained as an array: $systemEvidenceJson"
            }
            Write-Output 'block-header-matrix-ok'
            """);

        Assert.True(result.ExitCode == 0, $"PowerShell failed. stdout: {result.StandardOutput}{Environment.NewLine}stderr: {result.StandardError}");
        Assert.Equal("block-header-matrix-ok", result.StandardOutput.Trim());
    }

    [Fact]
    public void BlockHeaderAcceptance_BlankCaseAllowsTypedVersionAndVerifiesDeclaredAbsences()
    {
        var result = RunHarnessFunctions(
            ["Assert-Condition", "Assert-BlockHeaderExpectationDocument", "Get-SnapshotNodes", "Resolve-NodeBySelector", "Assert-BlockHeaderExpectations"],
            """
            function Node($id, $parent, $type, $name, $details) {
                [pscustomobject]@{
                    nodeId = $id
                    parentNodeId = $parent
                    nodeType = $type
                    name = $name
                    details = $details
                }
            }
            function Segment($type, $name) { [pscustomobject]@{ nodeType = $type; name = $name } }
            function Case($id, $kind, $selector, $name, $details, $absent) {
                [pscustomobject]@{
                    id = $id
                    kind = $kind
                    selector = $selector
                    expected = [pscustomobject]@{
                        name = $name
                        details = $details
                        absentDetails = $absent
                    }
                }
            }

            $pages = @([pscustomobject]@{ result = [pscustomobject]@{ nodes = @(
                (Node 'device' $null 'Device' 'PLC_1' ([pscustomobject]@{})),
                (Node 'user' 'device' 'FB' 'UserEngineering' ([pscustomobject]@{
                    HeaderAuthor = 'Ada Lovelace'
                    HeaderFamily = 'Motion'
                    HeaderName = 'User Header'
                })),
                (Node 'system' 'device' 'FC' 'SystemFunction' ([pscustomobject]@{
                    IsSystemBlock = 'true'
                    HeaderName = 'System Header'
                })),
                (Node 'unit' 'device' 'FB' 'UnitFunction' ([pscustomobject]@{
                    SoftwareUnit = 'UnitA'
                    HeaderAuthor = 'Unit Author'
                })),
                (Node 'blank' 'device' 'FC' 'BlankHeaders' ([pscustomobject]@{
                    HeaderVersion = '0.1'
                    Number = '7'
                    ProgrammingLanguage = 'SCL'
                }))
            ) } })

            $device = Segment 'Device' 'PLC_1'
            $expectations = [pscustomobject]@{
                schemaVersion = 'issue-30-block-header-expectations/v1'
                cases = @(
                    (Case 'user-fields' 'user' @($device, (Segment 'FB' 'UserEngineering')) 'UserEngineering' ([pscustomobject]@{
                        HeaderAuthor = 'Ada Lovelace'
                        HeaderFamily = 'Motion'
                        HeaderName = 'User Header'
                    }) @()),
                    (Case 'system-field' 'system' @($device, (Segment 'FC' 'SystemFunction')) 'SystemFunction' ([pscustomobject]@{
                        HeaderName = 'System Header'
                    }) @()),
                    (Case 'software-unit-field' 'softwareUnit' @($device, (Segment 'FB' 'UnitFunction')) 'UnitFunction' ([pscustomobject]@{
                        HeaderAuthor = 'Unit Author'
                    }) @()),
                    (Case 'blank-version' 'blank' @($device, (Segment 'FC' 'BlankHeaders')) 'BlankHeaders' ([pscustomobject]@{
                        HeaderVersion = '0.1'
                    }) @('HeaderAuthor', 'HeaderFamily', 'HeaderName'))
                )
            }

            $evidence = Assert-BlockHeaderExpectations -Pages $pages -Expectations $expectations
            $blankEvidence = @($evidence.cases | Where-Object { $_.id -ceq 'blank-version' })[0]
            if ([string] $blankEvidence.verifiedDetails.HeaderVersion -cne '0.1') {
                throw 'The typed blank-case HeaderVersion was not verified.'
            }
            if (@($blankEvidence.absentDetails).Count -ne 3) {
                throw 'The blank case did not retain all declared absent details.'
            }
            foreach ($fieldName in @('HeaderAuthor', 'HeaderFamily', 'HeaderName')) {
                if ($fieldName -cnotin @($blankEvidence.absentDetails)) {
                    throw "The blank case did not verify declared absence of '$fieldName'."
                }
            }
            if (-not $evidence.blankHeadersOmitted) { throw 'Declared blank header omissions were not verified.' }
            Write-Output 'typed-blank-version-ok'
            """);

        Assert.True(result.ExitCode == 0, $"PowerShell failed. stdout: {result.StandardOutput}{Environment.NewLine}stderr: {result.StandardError}");
        Assert.Equal("typed-blank-version-ok", result.StandardOutput.Trim());
    }

    [Fact]
    public void BlockHeaderAcceptance_RejectsFourCasesThatOmitRequiredKinds()
    {
        var result = RunHarnessFunctions(
            ["Assert-Condition", "Assert-BlockHeaderExpectationDocument", "Get-SnapshotNodes", "Resolve-NodeBySelector", "Assert-BlockHeaderExpectations"],
            """
            $pages = @([pscustomobject]@{ result = [pscustomobject]@{ nodes = @(
                [pscustomobject]@{
                    nodeId = 'user'
                    parentNodeId = $null
                    nodeType = 'FB'
                    name = 'EngineeringName'
                    details = [pscustomobject]@{
                        HeaderAuthor = 'Ada'
                        HeaderVersion = '1.2'
                        HeaderFamily = 'Motion'
                        HeaderName = 'Header Name'
                    }
                }
            ) } })
            $fields = @('HeaderAuthor', 'HeaderVersion', 'HeaderFamily', 'HeaderName')
            $cases = for ($index = 0; $index -lt $fields.Count; $index++) {
                $details = [pscustomobject]@{}
                $details | Add-Member -NotePropertyName $fields[$index] -NotePropertyValue @('Ada', '1.2', 'Motion', 'Header Name')[$index]
                [pscustomobject]@{
                    id = "user-$index"
                    kind = 'user'
                    selector = @([pscustomobject]@{ nodeType = 'FB'; name = 'EngineeringName' })
                    expected = [pscustomobject]@{ name = 'EngineeringName'; details = $details; absentDetails = @() }
                }
            }
            $expectations = [pscustomobject]@{
                schemaVersion = 'issue-30-block-header-expectations/v1'
                cases = @($cases)
            }

            $rejected = $false
            try { $null = Assert-BlockHeaderExpectations -Pages $pages -Expectations $expectations }
            catch { $rejected = $_.Exception.Message -match 'missing case kinds' }
            if (-not $rejected) { throw 'The incomplete path-kind matrix was not rejected for the correct reason.' }
            Write-Output 'missing-kinds-rejected'
            """);

        Assert.True(result.ExitCode == 0, $"PowerShell failed. stdout: {result.StandardOutput}{Environment.NewLine}stderr: {result.StandardError}");
        Assert.Equal("missing-kinds-rejected", result.StandardOutput.Trim());
    }

    [Fact]
    public void BaselineComparison_IgnoresHeaderDetailsAndReportsObservedDeltas()
    {
        var result = RunHarnessFunctions(
            ["Assert-Condition", "Get-Sha256", "Get-MedianMilliseconds", "Get-TreeIdentityEvidence", "Get-MeasurementSpread", "Compare-ProjectTreeEvidence"],
            """
            function Node($name, $details) {
                [pscustomobject]@{
                    nodeId = 'block-1'
                    parentNodeId = 'folder-1'
                    sequence = 7
                    nodeType = 'FB'
                    name = $name
                    details = $details
                }
            }

            $baselineIdentity = Get-TreeIdentityEvidence -Nodes @(
                (Node 'EngineeringName' ([pscustomobject]@{ Number = '1' }))
            )
            $candidateIdentity = Get-TreeIdentityEvidence -Nodes @(
                (Node 'EngineeringName' ([pscustomobject]@{
                    Number = '1'
                    HeaderAuthor = 'Ada'
                    HeaderVersion = '1.2'
                    HeaderFamily = 'Motion'
                    HeaderName = 'Header Name'
                }))
            )
            if ($baselineIdentity.sha256 -cne $candidateIdentity.sha256) { throw 'Header details changed tree identity.' }

            $renamedIdentity = Get-TreeIdentityEvidence -Nodes @(
                (Node 'RenamedEngineeringObject' ([pscustomobject]@{ Number = '1' }))
            )
            if ($baselineIdentity.sha256 -ceq $renamedIdentity.sha256) { throw 'Engineering name did not change tree identity.' }

            $baseline = [pscustomobject]@{
                treeIdentity = $baselineIdentity
                timing = [pscustomobject]@{ runs = @(
                    [pscustomobject]@{ elapsedMs = 10.0; canonicalResponseChars = 100 },
                    [pscustomobject]@{ elapsedMs = 12.0; canonicalResponseChars = 110 },
                    [pscustomobject]@{ elapsedMs = 14.0; canonicalResponseChars = 120 }
                ) }
            }
            $candidate = [pscustomobject]@{
                treeIdentity = $candidateIdentity
                timing = [pscustomobject]@{ runs = @(
                    [pscustomobject]@{ elapsedMs = 14.0; canonicalResponseChars = 120 },
                    [pscustomobject]@{ elapsedMs = 16.0; canonicalResponseChars = 130 },
                    [pscustomobject]@{ elapsedMs = 18.0; canonicalResponseChars = 140 }
                ) }
            }

            $comparison = Compare-ProjectTreeEvidence -Candidate $candidate -Baseline $baseline
            if (-not $comparison.treeIdentityEqual) { throw 'Equal tree identities were rejected.' }
            if ($comparison.timingMs.baseline.min -ne 10.0 -or $comparison.timingMs.baseline.max -ne 14.0) { throw 'Baseline timing spread is incorrect.' }
            if ($comparison.timingMs.candidate.median -ne 16.0 -or $comparison.timingMs.medianDelta -ne 4.0) { throw 'Timing median delta is incorrect.' }
            if ($comparison.initialResponseChars.baseline.median -ne 110.0) { throw 'Baseline size median is incorrect.' }
            if ($comparison.initialResponseChars.candidate.max -ne 140.0 -or $comparison.initialResponseChars.medianDelta -ne 20.0) { throw 'Size median delta is incorrect.' }

            $baseline.treeIdentity.nodeCount = [int64] 4000000000
            $candidate.treeIdentity.nodeCount = [int64] 4000000000
            $wideCountComparison = Compare-ProjectTreeEvidence -Candidate $candidate -Baseline $baseline
            if (-not $wideCountComparison.treeIdentityEqual) { throw 'Comparison narrowed a valid Int64 node count.' }
            Write-Output 'baseline-comparison-ok'
            """);

        Assert.True(result.ExitCode == 0, $"PowerShell failed. stdout: {result.StandardOutput}{Environment.NewLine}stderr: {result.StandardError}");
        Assert.Equal("baseline-comparison-ok", result.StandardOutput.Trim());
    }

    [Fact]
    public void BaselineComparison_RejectsTreeIdentityDrift()
    {
        var result = RunHarnessFunctions(
            ["Assert-Condition", "Get-MedianMilliseconds", "Get-MeasurementSpread", "Compare-ProjectTreeEvidence"],
            """
            function Evidence($hash) {
                [pscustomobject]@{
                    treeIdentity = [pscustomobject]@{ nodeCount = 1; sha256 = $hash }
                    timing = [pscustomobject]@{ runs = @(
                        [pscustomobject]@{ elapsedMs = 10.0; canonicalResponseChars = 100 },
                        [pscustomobject]@{ elapsedMs = 11.0; canonicalResponseChars = 101 },
                        [pscustomobject]@{ elapsedMs = 12.0; canonicalResponseChars = 102 }
                    ) }
                }
            }
            $rejected = $false
            try { $null = Compare-ProjectTreeEvidence -Candidate (Evidence 'candidate') -Baseline (Evidence 'baseline') }
            catch { $rejected = $_.Exception.Message -match 'tree identities differ' }
            if (-not $rejected) { throw 'Tree identity drift was not rejected.' }
            Write-Output 'identity-drift-rejected'
            """);

        Assert.True(result.ExitCode == 0, $"PowerShell failed. stdout: {result.StandardOutput}{Environment.NewLine}stderr: {result.StandardError}");
        Assert.Equal("identity-drift-rejected", result.StandardOutput.Trim());
    }

    [Fact]
    public void BlockHeaderRunConfiguration_RequiresPairedCandidateEvidenceInputs()
    {
        var result = RunHarnessFunctions(
            ["Assert-Condition", "Resolve-BlockHeaderRunConfiguration"],
            """
            $baseline = Resolve-BlockHeaderRunConfiguration -AcceptanceProfile 'BlockHeaders' -ExpectationsPath '' -BaselinePath ''
            if ($baseline.mode -cne 'baseline') { throw 'No-input BlockHeaders run was not classified as baseline.' }

            $candidate = Resolve-BlockHeaderRunConfiguration -AcceptanceProfile 'BlockHeaders' -ExpectationsPath 'expected.json' -BaselinePath 'main.json'
            if ($candidate.mode -cne 'candidate') { throw 'Paired-input BlockHeaders run was not classified as candidate.' }

            $rejected = $false
            try {
                $null = Resolve-BlockHeaderRunConfiguration -AcceptanceProfile 'BlockHeaders' -ExpectationsPath 'expected.json' -BaselinePath ''
            }
            catch {
                $rejected = $_.Exception.Message -match 'both'
            }
            if (-not $rejected) { throw 'Unpaired candidate inputs were not rejected.' }

            $full = Resolve-BlockHeaderRunConfiguration -AcceptanceProfile 'FullV3' -ExpectationsPath '' -BaselinePath ''
            if ($full.mode -cne 'fullV3') { throw 'Default acceptance profile changed.' }

            $caseInsensitive = Resolve-BlockHeaderRunConfiguration -AcceptanceProfile 'blockheaders' -ExpectationsPath '' -BaselinePath ''
            if ($caseInsensitive.mode -cne 'baseline' -or $caseInsensitive.profile -cne 'BlockHeaders') {
                throw 'Accepted profile spelling was not canonicalized.'
            }
            Write-Output 'run-configuration-ok'
            """);

        Assert.True(result.ExitCode == 0, $"PowerShell failed. stdout: {result.StandardOutput}{Environment.NewLine}stderr: {result.StandardError}");
        Assert.Equal("run-configuration-ok", result.StandardOutput.Trim());
    }

    [Fact]
    public void LiveHarness_RejectsUnpairedBlockHeaderInputsBeforeStartingServer()
    {
        var projectPath = Path.GetTempFileName();
        try
        {
            var result = RunHarnessScript(
                "-AcceptanceProfile", "BlockHeaders",
                "-ProjectPath", projectPath,
                "-BlockHeaderExpectationsPath", "expected.json");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("requires both", result.StandardOutput + result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain("ServerPath", result.StandardOutput + result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(projectPath);
        }
    }

    [Fact]
    public void BlockHeaderExpectationExample_ContainsTheRequiredAcceptanceMatrix()
    {
        var path = RepositoryFile("scripts", "live-test-project-tree-block-header-expectations.example.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.Equal("issue-30-block-header-expectations/v1", root.GetProperty("schemaVersion").GetString());
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(
            ["blank", "softwareUnit", "system", "user"],
            cases.Select(item => item.GetProperty("kind").GetString()!).Order(StringComparer.Ordinal).ToArray());

        var verifiedDetails = cases
            .SelectMany(item => item.GetProperty("expected").GetProperty("details").EnumerateObject())
            .Select(property => property.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["HeaderAuthor", "HeaderFamily", "HeaderName", "HeaderVersion"], verifiedDetails);

        var userCase = Assert.Single(cases, item => item.GetProperty("kind").GetString() == "user");
        Assert.NotEqual(
            userCase.GetProperty("expected").GetProperty("name").GetString(),
            userCase.GetProperty("expected").GetProperty("details").GetProperty("HeaderName").GetString());

        var blankCase = Assert.Single(cases, item => item.GetProperty("kind").GetString() == "blank");
        Assert.Equal(
            "0.1",
            blankCase.GetProperty("expected").GetProperty("details").GetProperty("HeaderVersion").GetString());
        Assert.Equal(
            ["HeaderAuthor", "HeaderFamily", "HeaderName"],
            blankCase.GetProperty("expected").GetProperty("absentDetails").EnumerateArray()
                .Select(value => value.GetString()!).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void LiveHarness_RejectsUncustomizedExpectationsBeforeResolvingServer()
    {
        var projectPath = Path.GetTempFileName();
        var baselinePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(baselinePath, "{}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var result = RunHarnessScript(
                "-AcceptanceProfile", "BlockHeaders",
                "-ProjectPath", projectPath,
                "-ServerPath", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe"),
                "-BlockHeaderExpectationsPath", RepositoryFile("scripts", "live-test-project-tree-block-header-expectations.example.json"),
                "-BaselineEvidencePath", baselinePath);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Replace every REPLACE_ placeholder", result.StandardOutput + result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain("ServerPath", result.StandardOutput + result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(projectPath);
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void LiveHarness_RejectsInvalidBaselineBeforeResolvingServer()
    {
        var projectPath = Path.GetTempFileName();
        var expectationsPath = Path.GetTempFileName();
        var baselinePath = Path.GetTempFileName();
        try
        {
            var template = File.ReadAllText(RepositoryFile("scripts", "live-test-project-tree-block-header-expectations.example.json"));
            File.WriteAllText(expectationsPath, template.Replace("REPLACE_", "CUSTOM_", StringComparison.Ordinal), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(baselinePath, "{}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var result = RunHarnessScript(
                "-AcceptanceProfile", "BlockHeaders",
                "-ProjectPath", projectPath,
                "-ServerPath", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe"),
                "-BlockHeaderExpectationsPath", expectationsPath,
                "-BaselineEvidencePath", baselinePath);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Baseline evidence has the wrong schemaVersion", result.StandardOutput + result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain("ServerPath", result.StandardOutput + result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(projectPath);
            File.Delete(expectationsPath);
            File.Delete(baselinePath);
        }
    }

    [Theory]
    [InlineData("missing-kind", "require user, system, softwareUnit, and blank")]
    [InlineData("empty-selector", "empty selector")]
    [InlineData("scalar-selector", "selector must be a JSON array")]
    [InlineData("scalar-absent-details", "absentDetails must be a JSON array")]
    public void LiveHarness_RejectsStructurallyInvalidExpectationsBeforeResolvingServer(string defect, string expectedMessage)
    {
        var projectPath = Path.GetTempFileName();
        var expectationsPath = Path.GetTempFileName();
        var baselinePath = Path.GetTempFileName();
        try
        {
            var template = File.ReadAllText(RepositoryFile("scripts", "live-test-project-tree-block-header-expectations.example.json"));
            var expectations = JsonNode.Parse(template.Replace("REPLACE_", "CUSTOM_", StringComparison.Ordinal))!.AsObject();
            var cases = expectations["cases"]!.AsArray();
            if (defect == "missing-kind")
                cases.RemoveAt(cases.Count - 1);
            else if (defect == "empty-selector")
                cases[0]!["selector"] = new JsonArray();
            else if (defect == "scalar-selector")
                cases[0]!["selector"] = cases[0]!["selector"]![0]!.DeepClone();
            else
                cases[1]!["expected"]!["absentDetails"] = "HeaderFamily";

            File.WriteAllText(expectationsPath, expectations.ToJsonString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(baselinePath, CreateValidBlockHeaderBaselineJson(projectPath), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var result = RunHarnessScript(
                "-AcceptanceProfile", "BlockHeaders",
                "-ProjectPath", projectPath,
                "-ServerPath", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe"),
                "-BlockHeaderExpectationsPath", expectationsPath,
                "-BaselineEvidencePath", baselinePath);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(expectedMessage, result.StandardOutput + result.StandardError, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ServerPath", result.StandardOutput + result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(projectPath);
            File.Delete(expectationsPath);
            File.Delete(baselinePath);
        }
    }

    [Theory]
    [InlineData("missing-full-project", "fullProject")]
    [InlineData("two-runs", "exactly three")]
    [InlineData("nonnumeric-run", "invalid elapsedMs")]
    [InlineData("missing-worker-provenance", "opennessWorker")]
    [InlineData("oversized-node-count", "4,000,000")]
    [InlineData("oversized-response", "60,000")]
    [InlineData("uppercase-hash", "lowercase SHA-256")]
    public void LiveHarness_RejectsMalformedNestedBaselineBeforeResolvingServer(string defect, string expectedMessage)
    {
        var projectPath = Path.GetTempFileName();
        var expectationsPath = Path.GetTempFileName();
        var baselinePath = Path.GetTempFileName();
        try
        {
            var template = File.ReadAllText(RepositoryFile("scripts", "live-test-project-tree-block-header-expectations.example.json"));
            File.WriteAllText(expectationsPath, template.Replace("REPLACE_", "CUSTOM_", StringComparison.Ordinal), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var baseline = JsonNode.Parse(CreateValidBlockHeaderBaselineJson(projectPath))!.AsObject();
            var fullProject = baseline["modes"]!["fullProject"];
            if (defect == "missing-full-project")
                baseline["modes"]!.AsObject().Remove("fullProject");
            else if (defect == "two-runs")
                fullProject!["timing"]!["runs"]!.AsArray().RemoveAt(2);
            else if (defect == "nonnumeric-run")
                fullProject!["timing"]!["runs"]![0]!["elapsedMs"] = "slow";
            else if (defect == "missing-worker-provenance")
                baseline["runtimeArtifacts"]!.AsObject().Remove("opennessWorker");
            else if (defect == "oversized-node-count")
            {
                fullProject!["treeIdentity"]!["nodeCount"] = 4_000_001;
                fullProject["snapshot"]!["totalNodes"] = 4_000_001;
            }
            else if (defect == "oversized-response")
                fullProject!["timing"]!["runs"]![0]!["canonicalResponseChars"] = 60_001;
            else
                fullProject!["treeIdentity"]!["sha256"] = new string('C', 64);

            File.WriteAllText(baselinePath, baseline.ToJsonString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var result = RunHarnessScript(
                "-AcceptanceProfile", "BlockHeaders",
                "-ProjectPath", projectPath,
                "-ServerPath", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe"),
                "-BlockHeaderExpectationsPath", expectationsPath,
                "-BaselineEvidencePath", baselinePath);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(expectedMessage, result.StandardOutput + result.StandardError, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ServerPath", result.StandardOutput + result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(projectPath);
            File.Delete(expectationsPath);
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void LiveHarness_RejectsEvidenceInputPathCollisionsBeforeResolvingServer()
    {
        var projectPath = Path.GetTempFileName();
        var expectationsPath = Path.GetTempFileName();
        var baselinePath = Path.GetTempFileName();
        try
        {
            var template = File.ReadAllText(RepositoryFile("scripts", "live-test-project-tree-block-header-expectations.example.json"));
            var expectationsJson = template.Replace("REPLACE_", "CUSTOM_", StringComparison.Ordinal);
            File.WriteAllText(expectationsPath, expectationsJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(baselinePath, CreateValidBlockHeaderBaselineJson(projectPath), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var result = RunHarnessScript(
                "-AcceptanceProfile", "BlockHeaders",
                "-ProjectPath", projectPath,
                "-ServerPath", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe"),
                "-EvidencePath", expectationsPath,
                "-BlockHeaderExpectationsPath", expectationsPath,
                "-BaselineEvidencePath", baselinePath);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("must not overwrite", result.StandardOutput + result.StandardError, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ServerPath", result.StandardOutput + result.StandardError, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(expectationsJson, File.ReadAllText(expectationsPath));
        }
        finally
        {
            File.Delete(projectPath);
            File.Delete(expectationsPath);
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void RuntimeArtifactProvenance_HashesTheHostAndDeployedWorker()
    {
        var result = RunHarnessFunctions(
            ["Assert-Condition", "Get-RuntimeArtifactProvenance"],
            """
            $root = Join-Path ([IO.Path]::GetTempPath()) ('runtime-artifacts-' + [guid]::NewGuid().ToString('N'))
            $workerDirectory = Join-Path $root 'openness-worker'
            try {
                [void] (New-Item -ItemType Directory -Path $workerDirectory -Force)
                $hostPath = Join-Path $root 'TiaMcpServer.exe'
                $workerPath = Join-Path $workerDirectory 'TiaMcpServer.OpennessWorker.exe'
                [IO.File]::WriteAllText($hostPath, 'host')
                [IO.File]::WriteAllText($workerPath, 'worker')

                $provenance = Get-RuntimeArtifactProvenance -ResolvedServerPath $hostPath
                if ($provenance.host.sha256 -cne (Get-FileHash -LiteralPath $hostPath -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Host hash mismatch.' }
                if ($provenance.opennessWorker.sha256 -cne (Get-FileHash -LiteralPath $workerPath -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Worker hash mismatch.' }
                if ([string] $provenance.opennessWorker.path -cne [IO.Path]::GetFullPath($workerPath)) { throw 'Worker path mismatch.' }
                Write-Output 'runtime-provenance-ok'
            }
            finally {
                Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            }
            """);

        Assert.True(result.ExitCode == 0, $"PowerShell failed. stdout: {result.StandardOutput}{Environment.NewLine}stderr: {result.StandardError}");
        Assert.Equal("runtime-provenance-ok", result.StandardOutput.Trim());
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

    [Theory]
    [InlineData("continuation", 1, "candidate", "Expected a succeeded response")]
    [InlineData("continuation", 2, "baseline", "Expected a succeeded response")]
    [InlineData("header", 1, "candidate", "expected 'HeaderAuthor'")]
    [InlineData("header", 2, "candidate", "expected 'HeaderAuthor'")]
    [InlineData("structure", 1, "candidate", "sequence continuity")]
    [InlineData("identity", 1, "candidate", "tree identities differ")]
    [InlineData("identity", 2, "baseline", "tree identities differ")]
    [InlineData("none", 0, "candidate", "")]
    [InlineData("none", 0, "baseline", "")]
    public void BlockHeaderMeasuredRuns_ValidateEveryCompleteSnapshot(string defect, int defectiveRun, string mode, string expectedError)
    {
        var result = RunHarnessFunctions([], BlockHeaderBehaviorFixture + $$"""
            $runConfiguration = [pscustomobject]@{ mode = '{{mode}}' }
            $blockHeaderExpectations = $expectations
            $runtimeArtifacts = [pscustomobject]@{}
            $evidence = [ordered]@{
                modes = [ordered]@{}; blockHeaders = $null; baselineComparison = $null
                status = 'running'; error = $null; toolNames = @()
            }
            $baselineEvidence = [pscustomobject]@{
                runtimeArtifacts = $runtimeArtifacts
                modes = [pscustomobject]@{ fullProject = [pscustomobject]@{
                    treeIdentity = Get-TreeIdentityEvidence -Nodes $nodes
                    timing = [pscustomobject]@{ runs = @(1..3 | ForEach-Object {
                        [pscustomobject]@{ elapsedMs = 1; canonicalResponseChars = 2 }
                    }) }
                } }
            }
            $script:initialCalls = 0
            $script:continuedRuns = [Collections.Generic.List[int]]::new()
            function Connect-McpServer { $script:ToolNames = @('browse_project_tree') }
            function Stop-McpServer { $script:stopped = $true }
            function Write-EvidenceArtifacts { param($Evidence); $script:savedEvidence = $Evidence }
            function Invoke-McpTool {
                param($Name, $Arguments)
                if ($Name -cne 'browse_project_tree') { throw 'Unexpected tool call.' }
                if ($Arguments.ContainsKey('cursor')) {
                    $run = [int] $Arguments.cursor
                    if ($script:continuedRuns.Contains($run)) { throw 'Repeated continuation.' }
                    $script:continuedRuns.Add($run)
                    $page = New-BehaviorPage $run 1 @($nodes | Select-Object -Skip 1) $null
                    if ($run -eq {{defectiveRun}}) {
                        switch ('{{defect}}') {
                            'continuation' { $page.status = 'failed' }
                            'header' { $page.result.nodes[0].details.HeaderAuthor = 'Wrong author' }
                            'structure' { $page.result.nodes[0].sequence = 99 }
                            'identity' { $page.result.nodes[-1].nodeId = 'changed-blank-id' }
                        }
                    }
                    return $page
                }
                $script:initialCalls++
                if ($script:initialCalls -gt 3) { throw 'Too many initial calls.' }
                New-BehaviorPage $script:initialCalls 0 @($nodes[0]) ([string] $script:initialCalls)
            }
            # Execute the real orchestration and its failure-evidence path; never start TIA.
            $orchestration = @($ast.EndBlock.Statements | Where-Object {
                $_ -is [System.Management.Automation.Language.TryStatementAst]
            })
            if ($orchestration.Count -ne 1) { throw 'Expected one top-level orchestration.' }
            $script:stopped = $false
            $script:savedEvidence = $null
            $caught = ''
            try { Invoke-Expression $orchestration[0].Extent.Text }
            catch { $caught = $_.Exception.Message }
            if (-not $script:stopped -or $null -eq $script:savedEvidence) { throw 'Failure evidence/cleanup did not execute.' }
            if ($script:initialCalls -ne 3) { throw 'Did not measure all three initial requests.' }
            if ('{{defect}}' -cne 'none') {
                if ($evidence.status -cne 'failed') { throw 'Earlier measured run defect was accepted.' }
                if (-not $caught.Contains({{PowerShellLiteral(expectedError)}})) { throw "Unexpected rejection: $caught" }
                if ($evidence.error -cne $caught) { throw 'Failure reason was not preserved.' }
                "rejected-{{defect}}-run-{{defectiveRun}}: $caught"
            }
            else {
                if ($evidence.status -cne 'succeeded') { throw "Valid measured runs failed: $caught" }
                if (($script:continuedRuns -join ',') -cne '1,2,3') { throw 'Did not complete all three independent cursor chains.' }
                if ($evidence.modes.fullProject.timing.runs.Count -ne 3) { throw 'Lost initial timing/size evidence.' }
                if ($evidence.modes.fullProject.snapshot.pageCount -ne 2) { throw 'Lost complete snapshot evidence.' }
                if ('{{mode}}' -ceq 'candidate' -and (-not $evidence.blockHeaders.blankHeadersOmitted -or -not $evidence.baselineComparison.treeIdentityEqual)) {
                    throw 'Lost existing candidate evidence fields.'
                }
                'all-measured-runs-ok'
            }
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
    }

    [Theory]
    [InlineData("Device", false)]
    [InlineData("BlockFolder", false)]
    [InlineData("Device", true)]
    [InlineData("BlockFolder", true)]
    public void BlockHeaderBlankCase_RejectsContainersAtPreflightAndRuntime(string nodeType, bool runtimeOnly)
    {
        var result = RunHarnessFunctions([], BlockHeaderBehaviorFixture + $$"""
            Assert-BlockHeaderExpectationDocument -Expectations $expectations
            if (${{runtimeOnly.ToString().ToLowerInvariant()}}) {
                # Isolate the runtime gate from the separately tested preflight gate.
                function Assert-BlockHeaderExpectationDocument { param($Expectations) }
            }
            $blank = $expectations.cases[-1]
            $blank.expected.details = [pscustomobject]@{}
            if ('{{nodeType}}' -ceq 'Device') {
                $blank.selector = @([pscustomobject]@{ nodeType = 'Device'; name = 'PLC_1' })
                $blank.expected.name = 'PLC_1'
            }
            else {
                $blank.selector[-1].nodeType = 'BlockFolder'
                $nodes[-1].nodeType = 'BlockFolder'
                $nodes[-1].details = [pscustomobject]@{}
            }
            $caught = ''
            try {
                if (${{runtimeOnly.ToString().ToLowerInvariant()}}) {
                    $null = Assert-BlockHeaderExpectations -Pages @([pscustomobject]@{ result = [pscustomobject]@{ nodes = $nodes } }) -Expectations $expectations
                }
                else { Assert-BlockHeaderExpectationDocument -Expectations $expectations }
            }
            catch { $caught = $_.Exception.Message }
            if (-not $caught.Contains('functional PLC block')) { throw "Non-block blank case was not rejected by the type gate: $caught" }
            'container-rejected: ' + $caught
            """);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
    }

    private const string BlockHeaderBehaviorFixture = """
        foreach ($definition in $ast.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
        }, $true)) { Invoke-Expression $definition.Extent.Text }
        function Node($id, $parent, $type, $name, $sequence, $details) {
            [pscustomobject]@{ nodeId = $id; parentNodeId = $parent; nodeType = $type; name = $name; sequence = $sequence; details = $details }
        }
        function Case($kind, $type, $name, $details, $absent) {
            [pscustomobject]@{
                id = $kind; kind = $kind
                selector = @([pscustomobject]@{ nodeType = 'Device'; name = 'PLC_1' }, [pscustomobject]@{ nodeType = $type; name = $name })
                expected = [pscustomobject]@{ name = $name; details = $details; absentDetails = $absent }
            }
        }
        $nodes = @(
            Node 'device' $null 'Device' 'PLC_1' 0 ([pscustomobject]@{})
            Node 'user' 'device' 'FB' 'UserEngineering' 1 ([pscustomobject]@{ HeaderAuthor = 'Ada'; HeaderVersion = '1.2'; HeaderFamily = 'Motion'; HeaderName = 'User Header' })
            Node 'system' 'device' 'FC' 'SystemFunction' 2 ([pscustomobject]@{ IsSystemBlock = 'true'; HeaderName = 'System Header' })
            Node 'unit' 'device' 'FB' 'UnitFunction' 3 ([pscustomobject]@{ SoftwareUnit = 'UnitA'; HeaderAuthor = 'Unit Author' })
            Node 'blank' 'device' 'FC' 'BlankHeaders' 4 ([pscustomobject]@{ HeaderVersion = '0.1' })
        )
        $expectations = [pscustomobject]@{
            schemaVersion = 'issue-30-block-header-expectations/v1'
            cases = @(
                Case 'user' 'FB' 'UserEngineering' ([pscustomobject]@{ HeaderAuthor = 'Ada'; HeaderVersion = '1.2'; HeaderFamily = 'Motion'; HeaderName = 'User Header' }) @()
                Case 'system' 'FC' 'SystemFunction' ([pscustomobject]@{ HeaderName = 'System Header' }) @()
                Case 'softwareUnit' 'FB' 'UnitFunction' ([pscustomobject]@{ HeaderAuthor = 'Unit Author' }) @()
                Case 'blank' 'FC' 'BlankHeaders' ([pscustomobject]@{ HeaderVersion = '0.1' }) @('HeaderAuthor', 'HeaderFamily', 'HeaderName')
            )
        }
        function New-BehaviorPage($run, $offset, $pageNodes, $cursor) {
            # Clone each response so a defect in one run cannot mutate a later run.
            [pscustomobject]@{
                contractVersion = '3.0'; status = 'succeeded'; failure = $null
                __contentText = '{}'; __structuredJson = '{}'
                result = [pscustomobject]@{
                    snapshot = [pscustomobject]@{ snapshotId = "snapshot-$run"; totalNodes = 5 }
                    pagination = [pscustomobject]@{ offset = $offset; returnedCount = @($pageNodes).Count; nextCursor = $cursor }
                    nodes = @($pageNodes | ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20)
                }
            }
        }

        """;

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

    private static ScriptResult RunHarnessScript(params string[] arguments)
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
        startInfo.ArgumentList.Add(RepositoryFile("scripts", "live-test-project-tree-v3.ps1"));
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start pwsh process.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
            throw new TimeoutException("Live harness validation did not exit within 30 seconds.");
        }
        if (!Task.WhenAll(standardOutput, standardError).Wait(5_000))
            throw new TimeoutException("Live harness validation output streams did not close within 5 seconds.");
        return new ScriptResult(process.ExitCode, standardOutput.GetAwaiter().GetResult(), standardError.GetAwaiter().GetResult());
    }

    private static string CreateValidBlockHeaderBaselineJson(string projectPath)
        => JsonSerializer.Serialize(new
        {
            schemaVersion = "issue-30-block-header-metadata-live/v1",
            acceptanceProfile = "BlockHeaders",
            runMode = "baseline",
            status = "succeeded",
            projectPath = Path.GetFullPath(projectPath),
            runtimeArtifacts = new
            {
                host = new { path = @"C:\Builds\main\TiaMcpServer.exe", sha256 = new string('a', 64) },
                opennessWorker = new { path = @"C:\Builds\main\openness-worker\TiaMcpServer.OpennessWorker.exe", sha256 = new string('b', 64) },
            },
            modes = new
            {
                fullProject = new
                {
                    snapshot = new { totalNodes = 1 },
                    treeIdentity = new { nodeCount = 1, sha256 = new string('c', 64) },
                    timing = new
                    {
                        runs = new[]
                        {
                            new { elapsedMs = 10.0, canonicalResponseChars = 100 },
                            new { elapsedMs = 11.0, canonicalResponseChars = 101 },
                            new { elapsedMs = 12.0, canonicalResponseChars = 102 },
                        },
                    },
                },
            },
        });

    private static string PowerShellLiteral(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string RepositoryFile(params string[] pathSegments)
        => Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(pathSegments).ToArray()));

    private sealed record ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
