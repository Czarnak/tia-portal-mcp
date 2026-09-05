using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

// Execution-free source checks only. Never start, dot-source, or parse the live harness here.
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

    private static string ReadScript()
    {
        Assert.True(File.Exists(ScriptPath), $"Expected the live harness at '{ScriptPath}'.");
        return File.ReadAllText(ScriptPath);
    }
}
