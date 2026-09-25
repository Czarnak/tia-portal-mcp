using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace TiaMcpServer.Tests.Network;

// Offline source/AST contracts and extracted pure helpers. Never launches the live harness or TIA.
public sealed class NetworkIoSystemQualificationLiveHarnessScriptTests
{
    private static string Root => FindRoot();
    private static string Source => File.ReadAllText(Path.Combine(Root, "scripts/live-test-network-phase5-qualification.ps1"));

    [Fact]
    public void DefaultsToReadOnlyAndRejectsEffectsBeforeProcessLaunch()
    {
        Assert.Contains("[string] $Mode = 'Inventory'", Source);
        Assert.Contains("$Mode -in @('Compile', 'Apply')", Source);
        Assert.Contains("-not $AllowEffectfulQualification", Source);
        Assert.True(Source.IndexOf("Effectful qualification requires") < Source.IndexOf("function Start-Child"));
    }

    [Theory]
    [InlineData("PN-A", "QUALIFY FIXTURE B Apply")]
    [InlineData("DP-A", "QUALIFY FIXTURE B Apply")]
    [InlineData("PN-B", "QUALIFY FIXTURE A Apply")]
    [InlineData("DP-B", "QUALIFY FIXTURE A Apply")]
    public async Task EffectfulGateRejectsWrongFixturePhraseBeforeManifestOrProcess(string alias, string phrase)
    {
        var result = await RunHarnessBeforeManifestAsync(alias, phrase);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Effectful qualification requires its exact confirmation phrase.", result.Output);
        Assert.DoesNotContain("qualification stopped", result.Output);
    }

    [Fact]
    public async Task EffectfulGateRejectsCaseVariantAliasBeforeManifestOrProcess()
    {
        var result = await RunHarnessBeforeManifestAsync("pn-b", "QUALIFY FIXTURE B Apply");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Fixture alias must use an exact closed value.", result.Output);
        Assert.DoesNotContain("qualification stopped", result.Output);
    }

    [Theory]
    [InlineData("apply", "QUALIFY FIXTURE B apply")]
    [InlineData("cOmPiLe", "QUALIFY FIXTURE B cOmPiLe")]
    [InlineData("preview", "unused")]
    public async Task RejectsCaseVariantModeBeforeManifestOrProcess(string mode, string phrase)
    {
        var result = await RunHarnessBeforeManifestAsync("PN-B", phrase, mode);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Mode must use an exact closed value.", result.Output);
        Assert.DoesNotContain("qualification stopped", result.Output);
    }

    [Theory]
    [InlineData("$false", false)]
    [InlineData("$true", true)]
    [InlineData("$null", true)]
    [InlineData("'False'", true)]
    public async Task BaselineRequiresBooleanFalseBeforeInspection(string modifiedValue, bool expectRejection)
        => await AssertBaselinePreflightDecisionAsync($"@{{ isModified = {modifiedValue}; isOpen = $true; path = $ProjectPath }}", expectRejection);

    [Theory]
    [InlineData("$true", "$ProjectPath", false)]
    [InlineData("$false", "$ProjectPath", true)]
    [InlineData("'True'", "$ProjectPath", true)]
    [InlineData("$true", "'C:\\Other\\Fixture.ap21'", true)]
    [InlineData("$true", "$null", true)]
    public async Task BaselineRequiresExactOpenProjectBeforeInspection(string isOpen, string path, bool expectRejection)
        => await AssertBaselinePreflightDecisionAsync($"@{{ isModified = $false; isOpen = {isOpen}; path = {path} }}", expectRejection);

    private static async Task AssertBaselinePreflightDecisionAsync(string projectLiteral, bool expectRejection)
    {
        var start = Source.IndexOf("$publicBefore = Get-PublicStatus", StringComparison.Ordinal);
        var end = Source.IndexOf("$inspection = Get-PublicInspection", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Expected a public-status preflight before inspection.");
        var preflight = Convert.ToBase64String(Encoding.Unicode.GetBytes(Source[start..end]));
        await RunOfflinePowerShellAsync($$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $ProjectPath = 'C:\Synthetic\Fixture.ap21'
            $status = @{ projectPath = $ProjectPath; project = {{projectLiteral}} }
            function Get-PublicStatus { return $status }
            $preflight = [scriptblock]::Create([Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{{preflight}}')))
            $rejected = $false
            try { & $preflight } catch {
                if ($_.Exception.Message -ne 'Qualification requires an unmodified project baseline.') { throw }
                $rejected = $true
            }
            if ($rejected -ne ${{expectRejection.ToString().ToLowerInvariant()}}) { throw 'Unexpected baseline decision.' }
            """);
    }

    [Theory]
    [InlineData("Compile", "", false)]
    [InlineData("Apply", "", false)]
    [InlineData("Compile", "$record.effect.mode = 'setAndCompile'", true)]
    [InlineData("Compile", "$record.effect.mutationCommitted = $true", true)]
    [InlineData("Compile", "$record.effect.appliedTarget.number = 2", true)]
    [InlineData("Compile", "$record.effect.after = @($record.effect.before[0])", true)]
    [InlineData("Apply", "$record.effect.mode = 'compileBaseline'", true)]
    [InlineData("Apply", "$record.effect.mutationCommitted = $false", true)]
    [InlineData("Apply", "$record.effect.appliedTarget = $null", true)]
    [InlineData("Apply", "$record.effect.appliedTarget.number = 1", true)]
    [InlineData("Apply", "$record.effect.after = @()", true)]
    [InlineData("Apply", "$record.effect.after[1].value.integerValue = 3", true)]
    [InlineData("Apply", "$record.effect.after[4].name = 'Number'", true)]
    [InlineData("Apply", "$record.effect.originalTarget.number = 8", true)]
    [InlineData("Apply", "$record.effect.ownerTarget.deviceName = 'Other'", false)]
    [InlineData("Apply", "$record.effect.ownerTarget = $null", true)]
    [InlineData("Apply", "$record.effect.ownerMatchCount = 2", true)]
    [InlineData("Apply", "$record.effect.hardwareTargetKind = 'device'", true)]
    [InlineData("Apply", "$record.effect.before = @($record.effect.before[0..3])", true)]
    public async Task CompletedEffectRequiresModeCommitAndTypedOwnershipProof(string mode, string mutation, bool expectRejection)
    {
        var start = Source.IndexOf("Assert-Same $sessionIdentity $effect.sessionIdentity", StringComparison.Ordinal);
        var after = Source.IndexOf("$after = Get-WorkerStatus", start, StringComparison.Ordinal);
        var end = after < 0 ? -1 : Source.LastIndexOf("    }", after, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Expected effect verdict before post-status read.");
        var verdict = Convert.ToBase64String(Encoding.Unicode.GetBytes(Source[start..end]));
        var path = Path.Combine(Root, "scripts/live-test-network-phase5-qualification.ps1").Replace("'", "''");
        await RunOfflinePowerShellAsync($$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{path}}', [ref]$tokens, [ref]$errors)
            if ($errors.Count) { throw 'Harness parse failed.' }
            foreach ($name in @('Get-Json', 'Assert-Same', 'Assert-FiveQualificationAttributes', 'Assert-EffectEvidence')) {
                $functions = @($ast.FindAll({ param($node)
                    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name
                }, $true))
                if ($functions.Count -gt 1) { throw 'Duplicate offline guard helper.' }
                if ($functions.Count -eq 1) { . ([scriptblock]::Create($functions[0].Extent.Text)) }
            }
            $Mode = '{{mode}}'
            $sessionIdentity = @{ workerSessionId = 'synthetic' }
            $effect = @{ sessionIdentity = $sessionIdentity }
            $canonical = @{ kind = 'ioSystem'; subnetId = 'synthetic-subnet'; number = 1; ioSystemName = $null }
            $applied = @{ kind = 'ioSystem'; subnetId = 'synthetic-subnet'; number = 2; ioSystemName = $null }
            $hardware = @{ kind = 'deviceItem'; deviceName = 'Synthetic'; itemPath = @(@{ index = 0; name = 'Controller'; positionNumber = 0; typeIdentifier = '' }) }
            $before = @(
                @{ name = 'Name'; available = $true; writable = $true; supportedTypes = @('System.String'); value = @{ kind = 'string'; stringValue = 'before' } },
                @{ name = 'Number'; available = $true; writable = $true; supportedTypes = @('System.Int32'); value = @{ kind = 'integer'; integerValue = 1 } },
                @{ name = 'MultipleUseIoSystem'; available = $true; writable = $true; supportedTypes = @('System.Boolean'); value = @{ kind = 'boolean'; booleanValue = $false } },
                @{ name = 'UseIoSystemNameAsDeviceNameExtension'; available = $true; writable = $true; supportedTypes = @('System.Boolean'); value = @{ kind = 'boolean'; booleanValue = $false } },
                @{ name = 'MaxNumberIWlanLinksPerSegment'; available = $false; writable = $false; supportedTypes = @(); value = $null }
            )
            $afterSnapshot = ConvertFrom-Json (Get-Json $before) -AsHashtable -Depth 100
            $afterSnapshot[1].value.integerValue = 2
            $proposal = @{ attributeName = 'Number'; expectedValue = @{ kind = 'integer'; integerValue = 1 }; desiredValue = @{ kind = 'integer'; integerValue = 2 } }
            $owner = @{ originalTarget = $canonical; ownerTarget = $hardware; before = $before }
            $record = @{ effect = @{
                mode = $(if ($Mode -eq 'Compile') { 'compileBaseline' } else { 'setAndCompile' })
                originalTarget = $canonical.Clone()
                appliedTarget = $(if ($Mode -eq 'Compile') { $canonical.Clone() } else { $applied.Clone() })
                ownerTarget = $hardware.Clone(); ownerMatchCount = 1; ownerIdentityVerified = $true
                hardwareTargetKind = 'deviceItem'; mutationCommitted = ($Mode -eq 'Apply')
                before = $before; after = @()
                compileState = 'Success'; errorCount = 0; evidenceOmitted = $false
            } }
            if ($Mode -eq 'Apply') { $record.effect.after = $afterSnapshot }
            {{mutation}}
            $verdict = [scriptblock]::Create([Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{{verdict}}')))
            $rejected = $false
            $failure = ''
            try { & $verdict } catch { $rejected = $true; $failure = $_.Exception.Message }
            if ($rejected -ne ${{expectRejection.ToString().ToLowerInvariant()}}) { throw "Unexpected effect evidence decision: $failure" }
            """);
    }

    [Theory]
    [InlineData("Compile", "Error")]
    [InlineData("Apply", "postCommitFailure")]
    public async Task CompletedEffectFailureCapturesPostStatusBeforeVerdict(string mode, string compileState)
        => await AssertEffectPostStatusDecisionAsync(mode, compileState, transportFails: false);

    [Fact]
    public async Task AmbiguousEffectTransportDoesNotReadPostStatus()
        => await AssertEffectPostStatusDecisionAsync("Apply", "notRequested", transportFails: true);

    private static async Task AssertEffectPostStatusDecisionAsync(string mode, string compileState, bool transportFails)
    {
        var start = Source.IndexOf("$effect = Invoke-Probe $request", StringComparison.Ordinal);
        var end = Source.IndexOf("$assertedCandidate = Assert-Candidate", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Expected effect handling before final candidate check.");
        // Include the effect block's closing brace so the extracted production path runs as a conditional.
        var block = Convert.ToBase64String(Encoding.Unicode.GetBytes("if ($true) {\n" + Source[start..end]));
        await RunOfflinePowerShellAsync($$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $Mode = '{{mode}}'
            $transportFails = ${{transportFails.ToString().ToLowerInvariant()}}
            $script:statusReads = 0
            $sessionIdentity = @{ workerSessionId = 'synthetic' }
            $effectResponse = @{ sessionIdentity = $sessionIdentity; payload = (@{ compileState = '{{compileState}}'; errorCount = 1; evidenceOmitted = $false } | ConvertTo-Json -Compress) }
            $record = @{}
            $owner = @{}; $proposal = @{}; $request = @{}; $before = @{}
            function Invoke-Probe { if ($transportFails) { throw 'Transport ended or timed out.' }; return $effectResponse }
            function Assert-Same { }
            function Assert-EffectEvidence { }
            function Get-WorkerStatus { $script:statusReads++; return @{ identity = $sessionIdentity; status = @{ marker = 'after' } } }
            $effectBlock = [scriptblock]::Create([Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{{block}}')))
            $failed = $false
            try { & $effectBlock } catch { $failed = $true }
            if (-not $failed) { throw 'Effect failure unexpectedly succeeded.' }
            if ($transportFails) {
                if ($script:statusReads -ne 0 -or $record.Contains('after')) { throw 'Ambiguous transport triggered a follow-up read.' }
            } else {
                if ($script:statusReads -ne 1 -or $record.after.status.marker -cne 'after') { throw 'Completed effect lost its post-status evidence.' }
            }
            """);
    }

    [Theory]
    [InlineData("PN-A", "QUALIFY FIXTURE A Apply", "Fixture A qualification stopped")]
    [InlineData("DP-A", "QUALIFY FIXTURE A Apply", "Fixture A qualification stopped")]
    [InlineData("PN-B", "QUALIFY FIXTURE B Apply", "Fixture B qualification stopped")]
    [InlineData("DP-B", "QUALIFY FIXTURE B Apply", "Fixture B qualification stopped")]
    public async Task EffectfulGateAcceptsOnlyMatchingFixturePhraseBeforeManifest(string alias, string phrase, string expectedFailure)
    {
        var result = await RunHarnessBeforeManifestAsync(alias, phrase);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expectedFailure, result.Output);
        Assert.DoesNotContain("Effectful qualification requires its exact confirmation phrase.", result.Output);
    }

    [Theory]
    [InlineData("PN-B", true)]
    [InlineData("DP-B", false)]
    public async Task FixtureBBooleanProposalIsQualifiedOnlyForPn(string alias, bool accepted)
    {
        var path = Path.Combine(Root, "scripts/live-test-network-phase5-qualification.ps1").Replace("'", "''");
        await RunOfflinePowerShellAsync($$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{path}}', [ref]$tokens, [ref]$errors)
            if ($errors.Count) { throw 'Harness parse failed.' }
            foreach ($name in @('Get-Json', 'Assert-Same', 'Assert-Keys', 'Assert-Proposal')) {
                $functions = @($ast.FindAll({ param($node)
                    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name
                }, $true))
                if ($functions.Count -ne 1) { throw 'Expected exactly one offline guard helper.' }
                . ([scriptblock]::Create($functions[0].Extent.Text))
            }
            $FixtureAlias = '{{alias}}'
            $proposal = @{ attributeName = 'MultipleUseIoSystem'; expectedValue = @{ kind = 'boolean'; booleanValue = $false }; desiredValue = @{ kind = 'boolean'; booleanValue = $true } }
            $owner = @{ before = @(@{ name = 'MultipleUseIoSystem'; available = $true; writable = $true; value = $proposal.expectedValue }) }
            $accepted = $true
            try { Assert-Proposal $proposal $owner } catch { $accepted = $false }
            if ($accepted -ne ${{accepted.ToString().ToLowerInvariant()}}) { throw 'Unexpected Fixture B proposal decision.' }
            """);
    }

    [Theory]
    [InlineData("IsPathFullyQualified($ProjectPath)")]
    [InlineData("$manifest.commit -cne $ExpectedCommit")]
    [InlineData("$manifest.tree -cne $ExpectedTree")]
    [InlineData("$ExpectedManifestSha256")]
    [InlineData("$ExpectedHarnessSha256")]
    [InlineData("Assert-Same $preview.durableIdentity $durableIdentity")]
    [InlineData("Assert-Same $preview.baseline $owner.before")]
    [InlineData("Assert-Same $preview.ownerTarget $owner.ownerTarget")]
    [InlineData("Assert-Same $preview.proposal $proposal")]
    [InlineData("Assert-Same $preview.binding $binding")]
    [InlineData("expectedSessionIdentity = $sessionIdentity")]
    public void HasExplicitFailClosedBindings(string guard) => Assert.Contains(guard, Source);

    [Fact]
    public void RejectsUnqualifiedOwnerUnsupportedOrMultipleFields()
    {
        Assert.Contains("$owner.ownerMatchCount -ne 1", Source);
        Assert.Contains("$owner.ownerIdentityVerified -ne $true", Source);
        Assert.Contains("$owner.ownerTarget.kind -cne 'deviceItem'", Source);
        Assert.Contains("Assert-Keys $proposal @('attributeName', 'expectedValue', 'desiredValue')", Source);
        Assert.Contains("$proposal.attributeName -cnotin $allowed", Source);
        Assert.Contains("$current.writable -ne $true", Source);
        Assert.Contains("Assert-Same $proposal.expectedValue $current.value", Source);
    }

    [Fact]
    public void RecordsIgnoredEvidenceAndUsesOnlyApprovedRoutes()
    {
        Assert.Contains("git -C $script:Root check-ignore", Source);
        Assert.Contains("'network_read'", Source);
        Assert.Contains("'probe_network_object_attributes'", Source);
        Assert.Contains("'probe_io_system_qualification'", Source);
        Assert.Contains("mode = 'inspectOwner'", Source);
        foreach (var forbidden in new[] { "network_write", "save_project", "download", "set_plc_mode", "compile_check" })
            Assert.DoesNotContain(forbidden, Source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("assertedCandidate = Assert-Candidate", Source);
        Assert.Contains("Stop. No automatic retry", Source);
    }

    [Fact]
    public async Task PowerShellParserAcceptsScriptWithoutExecutingIt()
    {
        _ = Source;
        var path = Path.Combine(Root, "scripts/live-test-network-phase5-qualification.ps1").Replace("'", "''");
        var command = $"$e=$null;$t=$null;$a=[System.Management.Automation.Language.Parser]::ParseFile('{path}',[ref]$t,[ref]$e);if($e.Count){{$e|% Message;exit 1}};if($a.ParamBlock.Parameters.Count -lt 10){{exit 2}}";
        var psi = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000)) { process.Kill(true); throw new TimeoutException("Parser timed out."); }
        Assert.True(process.ExitCode == 0, await output + await error);
    }

    [Fact]
    public void PublicInspectionExplicitlyRequestsAllFiveCandidateAttributes()
    {
        var start = Source.IndexOf("function Get-PublicInspection", StringComparison.Ordinal);
        var end = Source.IndexOf("function Get-Baseline", start, StringComparison.Ordinal);
        var inspection = Source[start..end];
        Assert.Contains("attributeNames = @('Name', 'Number', 'MultipleUseIoSystem', 'UseIoSystemNameAsDeviceNameExtension', 'MaxNumberIWlanLinksPerSegment')", inspection);
    }

    [Theory]
    [InlineData("$changed.attributes[4].availability = 'readFailed'")]
    [InlineData("$changed.attributes[4].access = 'none'")]
    [InlineData("$changed.attributes[4].availability = 'readFailed'; $changed.attributes[4].access = 'none'")]
    [InlineData("$changed.attributes[0].source = 'dynamic'")]
    [InlineData("$changed.attributes[0].value.typeName = 'System.Object'")]
    [InlineData("$changed.attributes[4].value = @{ kind = 'integer'; typeName = 'System.Int32'; value = 2 }")]
    [InlineData("$changed.attributes[4].diagnostic = 'Observation unavailable'")]
    [InlineData("$changed.attributes[0].supportedTypes = @('System.Object')")]
    [InlineData("$changed.attributes[0].value.value = 'changed'")]
    public async Task BaselineRejectsObservableMetadataDrift(string mutation)
    {
        await AssertPureBaselineHelperAsync($$"""
            {{mutation}}
            $rejected = $false
            try { Assert-Same (Get-Baseline $original) (Get-Baseline $changed) }
            catch { $rejected = $true }
            if (-not $rejected) { throw 'Observable baseline drift was accepted.' }
            """);
    }

    [Fact]
    public async Task BaselineAcceptsIdenticalObservations()
        => await AssertPureBaselineHelperAsync("Assert-Same (Get-Baseline $original) (Get-Baseline $changed)");

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public async Task CandidateRequiresFrozenWorkerConfig(bool includeConfigHash, bool changeConfig, bool expectRejection)
    {
        var temporary = Directory.CreateTempSubdirectory("phase5-config-guard-");
        try
        {
            var harness = Path.Combine(Root, "scripts/live-test-network-phase5-qualification.ps1").Replace("'", "''");
            var directory = temporary.FullName.Replace("'", "''");
            await RunOfflinePowerShellAsync($$"""
                Set-StrictMode -Version Latest
                $ErrorActionPreference = 'Stop'
                $tokens = $null; $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{harness}}', [ref]$tokens, [ref]$errors)
                if ($errors.Count) { throw 'Harness parse failed.' }
                # Extract the read-only candidate guard and hash helper, never process/IPC code.
                foreach ($name in @('Get-Sha', 'Assert-Candidate')) {
                    $functions = @($ast.FindAll({ param($node)
                        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name
                    }, $true))
                    if ($functions.Count -ne 1) { throw 'Expected one offline guard helper.' }
                    . ([scriptblock]::Create($functions[0].Extent.Text))
                }
                # Stub only Git identity/cleanliness; filesystem enumeration and SHA checks are real.
                function git {
                    $global:LASTEXITCODE = 0
                    if ($args -contains 'HEAD') { return 'candidate' }
                    if ($args -contains 'HEAD^{tree}') { return 'tree' }
                    if ($args -contains 'status') { return }
                    throw 'Unexpected git operation.'
                }
                $script:Root = '{{directory}}'
                $script:HarnessPath = '{{harness}}'
                $runtimeRoot = Join-Path $script:Root 'runtime'
                $null = New-Item -ItemType Directory -Path (Join-Path $runtimeRoot 'openness-worker')
                $files = @('host.dll', 'contracts.dll', 'host.runtimeconfig.json', 'openness-worker/TiaMcpServer.OpennessWorker.exe')
                $hashes = @{}
                foreach ($file in $files) {
                    $path = Join-Path $runtimeRoot $file
                    [IO.File]::WriteAllText($path, 'synthetic runtime fixture')
                    $hashes['runtime/' + $file] = Get-Sha $path
                }
                $config = Join-Path $runtimeRoot 'openness-worker/TiaMcpServer.OpennessWorker.exe.config'
                [IO.File]::WriteAllText($config, '<configuration><runtime /></configuration>')
                if (${{includeConfigHash.ToString().ToLowerInvariant()}}) {
                    $hashes['runtime/openness-worker/TiaMcpServer.OpennessWorker.exe.config'] = Get-Sha $config
                }
                $hostDll = Join-Path $runtimeRoot 'host.dll'
                $workerExe = Join-Path $runtimeRoot 'openness-worker/TiaMcpServer.OpennessWorker.exe'
                $ExpectedCommit = 'candidate'; $ExpectedTree = 'tree'
                $ProjectPath = Join-Path $script:Root 'synthetic.ap21'
                $ExpectedHarnessSha256 = Get-Sha $script:HarnessPath
                $manifest = @{ commit = $ExpectedCommit; tree = $ExpectedTree; projectPath = $ProjectPath; hostSha256 = (Get-Sha $hostDll); workerSha256 = (Get-Sha $workerExe); binaryHashes = $hashes }
                $ManifestPath = Join-Path $script:Root 'manifest.json'
                [IO.File]::WriteAllText($ManifestPath, ($manifest | ConvertTo-Json -Depth 10))
                $ExpectedManifestSha256 = Get-Sha $ManifestPath
                if (${{changeConfig.ToString().ToLowerInvariant()}}) {
                    if (-not (Assert-Candidate)) { throw 'Frozen candidate should initially pass.' }
                    [IO.File]::WriteAllText($config, '<configuration><runtime><assemblyBinding /></runtime></configuration>')
                }
                $rejected = $false
                try { $null = Assert-Candidate } catch { $rejected = $true }
                if ($rejected -ne ${{expectRejection.ToString().ToLowerInvariant()}}) { throw 'Unexpected config guard decision.' }
                """);
        }
        finally { temporary.Delete(recursive: true); }
    }

    private static async Task AssertPureBaselineHelperAsync(string assertion)
    {
        var path = Path.Combine(Root, "scripts/live-test-network-phase5-qualification.ps1").Replace("'", "''");
        var command = $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{path}}', [ref]$tokens, [ref]$errors)
            if ($errors.Count) { throw 'Harness parse failed.' }
            # Import only these pure helpers, never the harness body or process/IPC helpers.
            foreach ($name in @('Get-Json', 'Assert-Same', 'Get-Baseline')) {
                $functions = @($ast.FindAll({ param($node)
                    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name
                }, $true))
                if ($functions.Count -ne 1) { throw 'Expected exactly one pure helper.' }
                . ([scriptblock]::Create($functions[0].Extent.Text))
            }
            $original = @{ attributes = @(
                @{ name = 'Name'; availability = 'available'; access = 'readWrite'; source = 'modeled'; supportedTypes = @('System.String'); value = @{ kind = 'string'; typeName = 'System.String'; value = 'synthetic' }; diagnostic = $null },
                @{ name = 'Number'; availability = 'available'; access = 'readWrite'; source = 'modeled'; supportedTypes = @('System.Int32'); value = @{ kind = 'integer'; typeName = 'System.Int32'; value = 1 }; diagnostic = $null },
                @{ name = 'MultipleUseIoSystem'; availability = 'available'; access = 'readWrite'; source = 'dynamic'; supportedTypes = @('System.Boolean'); value = @{ kind = 'boolean'; typeName = 'System.Boolean'; value = $false }; diagnostic = $null },
                @{ name = 'UseIoSystemNameAsDeviceNameExtension'; availability = 'available'; access = 'readWrite'; source = 'dynamic'; supportedTypes = @('System.Boolean'); value = @{ kind = 'boolean'; typeName = 'System.Boolean'; value = $false }; diagnostic = $null },
                @{ name = 'MaxNumberIWlanLinksPerSegment'; availability = 'unknownAttribute'; access = 'unknown'; source = 'dynamic'; supportedTypes = @(); value = $null; diagnostic = $null }
            ) }
            $changed = ConvertFrom-Json (Get-Json $original) -AsHashtable -Depth 100
            {{assertion}}
            """;
        await RunOfflinePowerShellAsync(command);
    }

    private static async Task RunOfflinePowerShellAsync(string command)
    {
        var psi = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("Pure helper check timed out."); }
        Assert.True(process.ExitCode == 0, await output + await error);
    }

    private static async Task<(int ExitCode, string Output)> RunHarnessBeforeManifestAsync(string alias, string phrase, string mode = "Apply")
    {
        var psi = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] {
            "-NoProfile", "-File", Path.Combine(Root, "scripts/live-test-network-phase5-qualification.ps1"),
            "-Mode", mode, "-ProjectPath", Path.Combine(Path.GetTempPath(), "synthetic.ap21"),
            "-ManifestPath", Path.Combine(Root, "artifacts/live-network-phase5-pr2", Guid.NewGuid() + ".json"),
            "-FixtureAlias", alias, "-ExpectedCommit", "unused", "-ExpectedTree", "unused",
            "-ExpectedHarnessSha256", "unused", "-ExpectedManifestSha256", "unused",
            "-AllowEffectfulQualification", "-ConfirmationPhrase", phrase })
            psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("Prelaunch check timed out."); }
        return (process.ExitCode, await output + await error);
    }

    [Fact]
    public void PublicNetworkSurfaceContainsNoQualificationOperation()
    {
        foreach (var file in new[] { "NetworkReadTools.cs", "NetworkWriteTools.cs", "NetworkOperationCatalog.cs" })
        {
            var source = File.ReadAllText(Path.Combine(Root, "TiaMcpServer/Network", file));
            Assert.DoesNotContain("probe_io_system_qualification", source);
            Assert.DoesNotContain("IoSystemQualification", source);
        }
    }

    [Theory]
    [InlineData("NetworkReadTools.cs", "1AB4593461C493D68D5A8EE1658628DD85AC688B52E5B623EBDF94CBA7E86B41")]
    [InlineData("NetworkWriteTools.cs", "CD3F095E4CE2DCE2DC7A450B85F0AF639FC00B1BD1A41FEBB1098E0D141D9CED")]
    public void TemporaryQualificationDoesNotChangePublicToolDeclarationsOrSchemas(string file, string expected)
    {
        // Snapshot the existing declarations, including input/output schema types and descriptions.
        var bytes = Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(Root, "TiaMcpServer/Network", file)).Replace("\r\n", "\n"));
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.sln"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
}
