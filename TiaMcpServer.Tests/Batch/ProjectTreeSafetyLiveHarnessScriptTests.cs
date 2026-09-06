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
        foreach (var proof in new[] { "get_project_status", "$status.success", "$statusPayload.isOpen", "$statusPayload.path", "$status.sessionIdentity.projectPath", "OrdinalIgnoreCase", "Write-Artifact" })
            Assert.Contains(proof, binding);
        Assert.Contains("Resolve-Path -LiteralPath", Function(text, "Resolve-ProjectPath"));
        Assert.Contains("[IO.Path]::GetFullPath", Function(text, "Resolve-ProjectPath"));
        Assert.DoesNotContain("bindingState", text);
        Assert.DoesNotContain("connectionState", text);
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
