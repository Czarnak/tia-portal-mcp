using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class ProjectTreeSafetyLiveHarnessScriptTests
{
    [Fact]
    public void Script_DefaultsToInventoryAndExactStartupProjectBinding()
    {
        var text = ReadScript();
        Assert.Matches(@"\[ValidateSet\('Inventory', 'Preview', 'Apply'\)\]", text);
        Assert.Matches(@"\[string\]\s+\$Mode\s*=\s*'Inventory'", text);
        Assert.Contains("@('run', '--project', 'TiaMcpServer', '--', '--project', $ProjectPath)", text);
        Assert.Matches(@"Invoke-Mcp 'initialize'[\s\S]*'notifications/initialized'[\s\S]*Assert-VerifiedStartupBinding\s*\$inventory = Invoke-Mcp 'tools/list'", text);
    }

    [Fact]
    public void Script_VerifiesStartupBindingBeforeEveryGuardedTool()
    {
        var text = ReadScript();
        var gate = Function(text, "Invoke-Tool");
        Assert.Matches(@"'preview_write_batch', 'apply_write_batch', 'compile_check'[\s\S]*Assert-VerifiedStartupBinding[\s\S]*Invoke-Mcp 'tools/call'", gate);
        var binding = Function(text, "Assert-VerifiedStartupBinding");
        foreach (var proof in new[] { "get_project_status", "$status.success", "$statusPayload.success", "$statusPayload.project.isOpen", "$statusPayload.projectPath", "$statusPayload.project.path", "$status.sessionIdentity.projectPath", "OrdinalIgnoreCase", "Write-Artifact" })
            Assert.Contains(proof, binding);
        Assert.DoesNotContain("$statusPayload.isOpen", binding);
        Assert.DoesNotContain("$statusPayload.path", binding);
        Assert.Contains("Resolve-Path -LiteralPath", Function(text, "Resolve-ProjectPath"));
        Assert.Contains("[IO.Path]::GetFullPath", Function(text, "Resolve-ProjectPath"));
        Assert.DoesNotContain("bindingState", text);
        Assert.DoesNotContain("connectionState", text);
    }

    [Fact]
    public void Script_AllowsOnlyTheNormalFirstStatusConnectionWarning()
    {
        var gate = Function(ReadScript(), "Invoke-Tool");
        Assert.Contains("$Name -ceq 'get_project_status'", gate);
        Assert.Contains("$warnings.Count -eq 1", gate);
        Assert.Contains("^Connected to TIA Portal PID \\d+ with project '.+'\\.$", gate);
        Assert.Contains("Warnings prevent complete acceptance evidence.", gate);
    }

    [Fact]
    public void Script_UsesOnlyPublicRoutesAndNeverLifecycleMutation()
    {
        var text = ReadScript();
        foreach (var prohibited in new[] { "open_project", "save_project", "close_project", "create_project", "start_plc", "stop_plc", "OpennessWorker.exe" })
            Assert.DoesNotContain(prohibited, text);
        foreach (var route in new[] { "initialize", "notifications/initialized", "tools/list", "tools/call", "preview_write_batch", "apply_write_batch", "compile_check" })
            Assert.Contains("'" + route + "'", text);
    }

    [Fact]
    public void Script_ApplyRequiresAllowMutationAndExactAcknowledgement()
    {
        var text = ReadScript();
        Assert.Contains("[switch] $AllowMutation", text);
        Assert.Contains("$script:RequiredAcknowledgement = 'OVERRIDE BLOCKS AND DELETE GROUPS'", text);
        Assert.Contains("-cne $script:RequiredAcknowledgement", Function(text, "Assert-MutationAuthorization"));
        Assert.Contains("$Mode -cne 'Apply'", Function(text, "Assert-MutationAuthorization"));
        Assert.Single(Regex.Matches(text, @"confirm\s*=\s*\$true").Cast<Match>());
        Assert.Matches(@"Assert-MutationAuthorization[\s\S]*\$script:MutationStarted = \$true[\s\S]*Invoke-Tool 'apply_write_batch'", Function(text, "Invoke-Apply"));
    }

    [Fact]
    public void Script_RestoresAndProvesByteEquivalentContentBeforeFinalCompile()
    {
        var text = ReadScript();
        Assert.Contains("function Restore-ByteEquivalentProjectContent", text);
        Assert.Contains("function Assert-ByteEquivalentProjectContent", text);
        Assert.Contains("preApplyContentSha256", text);
        Assert.Contains("restoredContentSha256", text);
        Assert.DoesNotContain("discard", text, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"finally\s*\{\s*if \(\$script:MutationStarted\)[\s\S]*Restore-ByteEquivalentProjectContent[\s\S]*Assert-ByteEquivalentProjectContent[\s\S]*Invoke-CompileCheck", text);
        Assert.Contains("[Convert]::ToBase64String", Function(text, "Assert-ByteEquivalentProjectContent"));
        Assert.Contains("$Expected.Count -eq $Actual.Count", Function(text, "Assert-ByteEquivalentProjectContent"));
        Assert.Contains("$before.path -ceq $after.path -and $before.kind -ceq $after.kind", Function(text, "Assert-ByteEquivalentProjectContent"));
        Assert.Contains("[Convert]::ToBase64String($before.bytes) -ceq [Convert]::ToBase64String($after.bytes)", Function(text, "Assert-ByteEquivalentProjectContent"));
        Assert.Contains("$script:RestorationProven", Function(text, "Invoke-CompileCheck"));
        Assert.Contains("$payload.overallState -ceq 'Success'", Function(text, "Invoke-CompileCheck"));
        Assert.Contains("$payload.totalErrorCount -eq 0", Function(text, "Invoke-CompileCheck"));
    }

    [Fact]
    public void Script_UsesDeadlineReadsAndFinallyProcessCleanup()
    {
        var text = ReadScript();
        Assert.Contains("ReadLineAsync", text);
        Assert.Contains("WaitAsync($remaining)", text);
        Assert.Contains("$script:TransportHealthy = $false", text);
        Assert.Contains("ReadToEndAsync", text);
        Assert.Contains("CreateNoWindow = $true", text);
        Assert.Matches(@"finally[\s\S]*Stop-McpHost", text);
        Assert.Contains("Kill($true)", Function(text, "Stop-McpHost"));
    }

    [Fact]
    public void Script_ArtifactsDoNotPersistRawTokensOrUnredactedProcessOutput()
    {
        var text = ReadScript();
        Assert.Contains("Redact-SafetyToken", Function(text, "Write-Artifact"));
        Assert.Contains("[REDACTED]", Function(text, "Redact-SafetyToken"));
        Assert.Contains("ConvertFrom-Json", Function(text, "Redact-SafetyToken"));
        Assert.Contains("-Depth 100", Function(text, "Write-Artifact"));
        Assert.DoesNotContain("WriteAllText", Function(text, "Stop-McpHost"));
        Assert.All(text, character => Assert.InRange((int)character, 0, 127));
    }

    [Fact]
    public void Script_FailsClosedOnIncompleteOrUnsupportedRestorationBaseline()
    {
        var text = ReadScript();
        Assert.Contains("SCL", Function(text, "Read-ProjectContent"));
        Assert.Contains("FC", Function(text, "Read-ProjectContent"));
        Assert.Contains("get_block_content", Function(text, "Read-BlockBytes"));
        Assert.Contains("format = 'xml'", Function(text, "Read-BlockBytes"));
        Assert.Contains("update_block_logic", Function(text, "Restore-ByteEquivalentProjectContent"));
        Assert.Contains("create_block_group", Function(text, "Restore-ByteEquivalentProjectContent"));
        Assert.Contains("create_block", Function(text, "Restore-ByteEquivalentProjectContent"));
        Assert.Contains("Read-Tree $parentPath", Function(text, "Restore-ByteEquivalentProjectContent"));
        Assert.Matches(@"Read-ProjectContent[\s\S]*\$script:Baseline[\s\S]*Invoke-Apply", text);
    }

    [Fact]
    public void Script_RestoresOnlyAuthoritativeXmlButComparesCompleteExportBundles()
    {
        var text = ReadScript();
        var restore = Function(text, "Restore-ByteEquivalentProjectContent");
        Assert.Contains("$originalXml = Get-AuthoritativeXmlDocument $block.bytes", restore);
        Assert.Contains("yamlContent = $originalXml", restore);
        Assert.DoesNotContain("yamlContent = $original }", restore);
        var xml = Function(text, "Get-AuthoritativeXmlDocument");
        Assert.Contains("[regex]::Matches", xml);
        Assert.Contains("$xmlDocuments.Count -eq 1", xml);
        Assert.Contains("return $xmlDocuments[0]", xml);
        Assert.Contains("return ,$script:Utf8.GetBytes($content)", Function(text, "Read-BlockBytes"));
        Assert.Contains("$restored = Read-ProjectContent", Function(text, "Invoke-RestoredScenario"));
        Assert.Contains("Assert-ByteEquivalentProjectContent $script:Baseline $restored", Function(text, "Invoke-RestoredScenario"));
    }

    [Fact]
    public void Script_RelevantDriftRejectsOriginalTokenAndAlwaysRestoresTheDriftMutation()
    {
        var text = ReadScript();
        var relevant = Function(text, "Test-RelevantDriftRejection");
        Assert.Matches(@"New-Operation 'delete_block_group' \$FixtureGroupPath[\s\S]*\$originalPreview = Get-Preview[\s\S]*Invoke-Change \(New-Operation 'create_block_group' \$newGroupPath\)[\s\S]*Invoke-Apply \$target \$originalPreview -ExpectStateChanged", relevant);
        Assert.Contains("$originalPreview.currentStateHash -cne $driftPreview.currentStateHash", relevant);
        Assert.Contains("Assert-ByteEquivalentProjectContent $drifted (Read-ProjectContent)", relevant);
        Assert.Matches(
            @"if \(\$ExpectStateChanged\)\s*\{\s*Assert-Condition \(\$result\.success -eq \$false -and \$result\.failureCategory -ceq 'state_changed'\)",
            Function(text, "Invoke-Apply"));
        Assert.Contains("Invoke-RestoredScenario 'relevant-drift-rejection' { Test-RelevantDriftRejection }", text);
        AssertScenarioRestoration(text);
    }

    [Fact]
    public void Script_UnrelatedDriftAcceptsOriginalTokenAndRestoresBothMutations()
    {
        var text = ReadScript();
        var unrelated = Function(text, "Test-UnrelatedDriftAcceptance");
        Assert.Matches(@"New-Operation 'create_block' \$OccupiedBlockPath[\s\S]*\$originalPreview = Get-Preview[\s\S]*Invoke-Change \(New-Operation 'create_block_group' \$newGroupPath\)[\s\S]*Invoke-Apply \$target \$originalPreview", unrelated);
        Assert.Contains("$originalPreview.currentStateHash -ceq $driftPreview.currentStateHash", unrelated);
        Assert.Contains("Invoke-RestoredScenario 'unrelated-drift-acceptance' { Test-UnrelatedDriftAcceptance }", text);
        AssertScenarioRestoration(text);
    }

    private static void AssertScenarioRestoration(string text)
    {
        var scenario = Function(text, "Invoke-RestoredScenario");
        Assert.Matches(@"\$baseline = Read-ProjectContent[\s\S]*\$script:Baseline = \$baseline[\s\S]*try\s*\{\s*& \$Probe[\s\S]*finally\s*\{\s*if \(\$script:MutationStarted\)[\s\S]*Restore-ByteEquivalentProjectContent[\s\S]*Assert-ByteEquivalentProjectContent \$script:Baseline \$restored[\s\S]*\$script:RestorationProven = \$true[\s\S]*Invoke-CompileCheck", scenario);
    }

    [Theory]
    [InlineData("Test-OccupiedBlockContentDriftRejection", "occupied-block-content-drift-rejection", "create_block", "$OccupiedBlockPath")]
    [InlineData("Test-DescendantBlockContentDriftRejection", "descendant-block-content-drift-rejection", "delete_block_group", "$FixtureGroupPath")]
    public void Script_ContentOnlyDriftRejectsOriginalTokenWithUnchangedMembership(string function, string scenario, string operation, string path)
    {
        var text = ReadScript();
        var probe = Function(text, function);
        Assert.Contains("New-Operation '" + operation + "' " + path, probe);
        Assert.Matches(@"\$originalPreview = Get-Preview \$target[\s\S]*Invoke-BlockContentDrift \$OccupiedBlockPath[\s\S]*\$drifted = Read-ProjectContent[\s\S]*Assert-OnlyBlockContentChanged \$script:Baseline \$drifted \$OccupiedBlockPath[\s\S]*\$driftPreview = Get-Preview \$target[\s\S]*Invoke-Apply \$target \$originalPreview -ExpectStateChanged[\s\S]*Assert-ByteEquivalentProjectContent \$drifted \(Read-ProjectContent\)", probe);
        Assert.Contains("$originalPreview.currentStateHash -cne $driftPreview.currentStateHash", probe);
        Assert.Contains("Invoke-RestoredScenario '" + scenario + "' { " + function + " }", text);
        AssertScenarioRestoration(text);
        var controlled = Function(text, "Assert-OnlyBlockContentChanged");
        Assert.Contains("$Expected.Count -eq $Actual.Count", controlled);
        Assert.Contains("$before.path -ceq $after.path -and $before.kind -ceq $after.kind", controlled);
        Assert.Contains("[Convert]::ToBase64String($before.bytes) -cne [Convert]::ToBase64String($after.bytes)", controlled);
        Assert.Contains("Assert-ByteEquivalentProjectContent $expectedUnchanged $actualUnchanged", controlled);
        var mutation = Function(text, "Invoke-BlockContentDrift");
        Assert.Contains("New-Operation 'update_block_logic' $Path", mutation);
        Assert.DoesNotContain("New-Operation 'create_block'", mutation);
        Assert.DoesNotContain("New-Operation 'create_block_group'", mutation);
    }

    [Fact]
    public void Script_SameParentNameOccupancyDriftRejectsOriginalCreateGroupToken()
    {
        var text = ReadScript();
        var probe = Function(text, "Test-RequestedNameOccupancyDriftRejection");
        Assert.Matches(@"New-Operation 'create_block_group' \$newGroupPath[\s\S]*\$originalPreview = Get-Preview \$target[\s\S]*Invoke-Change \(New-Operation 'create_block_group' \$newGroupPath\)[\s\S]*\$drifted = Read-ProjectContent[\s\S]*Invoke-Apply \$target \$originalPreview -ExpectStateChanged[\s\S]*Assert-ByteEquivalentProjectContent \$drifted \(Read-ProjectContent\)", probe);
        Assert.Contains("$_.path -ceq $newGroupPath -and $_.kind -ceq 'BlockFolder'", probe);
        Assert.Contains("Assert-ByteEquivalentProjectContent $script:Baseline $withoutCollision", probe);
        Assert.Contains("$originalPreview.currentStateHash -cne $driftPreview.currentStateHash", probe);
        Assert.Contains("Invoke-RestoredScenario 'requested-name-occupancy-drift-rejection' { Test-RequestedNameOccupancyDriftRejection }", text);
        AssertScenarioRestoration(text);
    }

    private static string Function(string script, string name)
    {
        var match = Regex.Match(script, @"(?ms)^function " + Regex.Escape(name) + @"\b.*?(?=^function |^# Main|\z)");
        Assert.True(match.Success, "Missing function " + name);
        return match.Value;
    }

    private static string ReadScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, "scripts", "live-test-project-tree-safety-scopes.ps1"));
    }
}
