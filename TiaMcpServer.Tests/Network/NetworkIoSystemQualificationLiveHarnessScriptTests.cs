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
        Assert.Contains("$ConfirmationPhrase -cne \"QUALIFY FIXTURE A $Mode\"", Source);
        Assert.True(Source.IndexOf("Effectful qualification requires") < Source.IndexOf("function Start-Child"));
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
        Assert.Contains("$FixtureAlias -ceq 'DP-A'", Source);
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
