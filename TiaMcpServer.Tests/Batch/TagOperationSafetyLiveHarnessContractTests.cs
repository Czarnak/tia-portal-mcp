using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class TagOperationSafetyLiveHarnessContractTests
{
    [Fact]
    public void Script_IsPresent_RequiresPowerShell7AndStrictMode_AndDefaultsToNonMutatingMode()
    {
        var text = File.ReadAllText(ScriptPath);

        Assert.Matches(new Regex(@"^\s*#Requires\s+-Version\s+7(\.\d+)?\s*$", RegexOptions.Multiline), text);
        Assert.Contains("Set-StrictMode -Version Latest", text, StringComparison.Ordinal);
        Assert.Contains("$ErrorActionPreference = 'Stop'", text, StringComparison.Ordinal);
        Assert.Contains("[ValidateSet('PreviewOnly','DriftAndRestore','ApplyAndRestore')]", text, StringComparison.Ordinal);
        Assert.Contains("$Mode = 'PreviewOnly'", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_UsesTheHostRequiresExplicitMutationModeAndNeverRunsFromOrdinaryTests()
    {
        var text = File.ReadAllText(ScriptPath);

        Assert.Contains("TiaMcpServer", text, StringComparison.Ordinal);
        Assert.Contains("if ($Mode -eq 'ApplyAndRestore')", text, StringComparison.Ordinal);
        Assert.Contains("disposable project copy", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpennessWorker.exe", text, StringComparison.Ordinal);

        var testDirectory = Path.Combine(GetRepositoryRoot(), "TiaMcpServer.Tests");
        var thisFile = Path.Combine(testDirectory, "Batch", "TagOperationSafetyLiveHarnessContractTests.cs");
        var offendingFiles = Directory
            .EnumerateFiles(testDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(thisFile), StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("live-test-tag-operation-safety-scopes.ps1", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offendingFiles);
    }

    [Fact]
    public void Script_UsesFinallyCleanupArtifactHygieneAndTokenRedaction()
    {
        var text = File.ReadAllText(ScriptPath);

        Assert.Contains("try {", text, StringComparison.Ordinal);
        Assert.Contains("finally {", text, StringComparison.Ordinal);
        Assert.Contains("Stop-McpHost", text, StringComparison.Ordinal);
        Assert.Contains("artifact", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("failure.json", text, StringComparison.Ordinal);
        Assert.Contains("Redact-SafetyToken", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Host \"safetyToken:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_UsesAsyncStdoutReadsBoundedByTheRemainingDeadline()
    {
        var text = File.ReadAllText(ScriptPath);

        Assert.DoesNotContain("StandardOutput.ReadLine()", text, StringComparison.Ordinal);
        Assert.Contains("StandardOutput.ReadLineAsync()", text, StringComparison.Ordinal);
        Assert.Contains("$remaining = $deadline - (Get-Date)", text, StringComparison.Ordinal);
        Assert.Contains("$readTask.WaitAsync($remaining)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_CoversAllRequiredPr5LiveClaims()
    {
        var text = File.ReadAllText(ScriptPath);

        Assert.Contains("same-object drift", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("relevant collision", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unrelated sibling", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restore or discard", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Script_MutationGateRequiresDisposableCopyAndUsesGuardedNoSaveDiscard()
    {
        var text = File.ReadAllText(ScriptPath);

        Assert.Contains("[switch] $ConfirmDisposableCopy", text, StringComparison.Ordinal);
        Assert.Contains("$AllowMutation -and $ConfirmDisposableCopy", text, StringComparison.Ordinal);
        Assert.Contains("function Assert-MutationAuthorization", text, StringComparison.Ordinal);
        Assert.Contains("$AuthorizedProjectPath, $ProjectPath", text, StringComparison.Ordinal);
        Assert.Contains("$initialProject.isModified -eq $false", text, StringComparison.Ordinal);
        Assert.Contains("saveBeforeClose = $false", text, StringComparison.Ordinal);
        Assert.Contains("$closedPayload.project.isOpen -eq $false", text, StringComparison.Ordinal);
        Assert.DoesNotContain("'save_project'", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item -LiteralPath $ProjectPath", text, StringComparison.Ordinal);
    }

    private static readonly string ScriptPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "live-test-tag-operation-safety-scopes.ps1"));

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
