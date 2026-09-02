using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public class TagUpdateSafetyLiveHarnessContractTests
{
    private static readonly string RepositoryRoot = GetRepositoryRoot();
    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "scripts",
        "live-test-update-tag-safety.ps1");

    [Fact]
    public void Script_DefaultModeIsReadOnly()
    {
        var text = ReadScript();
        Assert.Matches(new Regex(@"\[ValidateSet\(\s*'Read'\s*,\s*'PreviewDrift'\s*,\s*'ApplyDrift'\s*,\s*'ProbeUnavailable'\s*\)\]"), text);
        Assert.Matches(new Regex(@"\[string\]\s*\$Mode\s*=\s*'Read'"), text);
    }

    [Fact]
    public void Script_ApplyDriftRequiresExplicitAuthorizationAndPreflightedReadableFlag()
    {
        var text = ReadScript();
        var applyGuard = text.IndexOf(
            "if ($Mode -eq 'ApplyDrift' -and -not $AllowApply)",
            StringComparison.Ordinal);
        var mainTry = text.IndexOf("try {\n    $script:WorkerProcess", StringComparison.Ordinal);

        Assert.True(applyGuard >= 0, "Expected an explicit ApplyDrift AllowApply guard.");
        Assert.True(mainTry > applyGuard, "AllowApply must be checked before the harness starts a child process.");
    }

    [Fact]
    public void Script_InternalSafetyReadCarriesObservedSessionIdentity()
    {
        var text = ReadScript();
        var identityReader = ExtractTopLevelFunction(text, "Get-CompleteSessionIdentity");
        var safetyReader = ExtractTopLevelFunction(text, "Read-UpdateTagSafetySnapshot");
        var mainTry = text.IndexOf("try {\n    $script:WorkerProcess", StringComparison.Ordinal);
        var statusCall = text.IndexOf("Get-CompleteSessionIdentity", mainTry, StringComparison.Ordinal);
        var firstSafetyRead = text.IndexOf("Read-UpdateTagSafetySnapshot", statusCall + 1, StringComparison.Ordinal);

        Assert.Contains("get_project_status", identityReader, StringComparison.Ordinal);
        Assert.Contains("$script:WorkerSessionIdentity = $identity", identityReader, StringComparison.Ordinal);
        Assert.Contains("expectedSessionIdentity = $script:WorkerSessionIdentity", safetyReader, StringComparison.Ordinal);
        Assert.DoesNotContain("expectedSessionIdentity = @{", safetyReader, StringComparison.Ordinal);
        Assert.True(statusCall >= 0 && firstSafetyRead > statusCall,
            "The entry point must establish the observed identity before any safety read.");
    }

    [Fact]
    public void Script_OptionalUnavailableProbeUsesSeparateTargetInputs()
    {
        var text = ReadScript();
        var probeGuard = ExtractTopLevelFunction(text, "Assert-OptionalProbeTargetIsDistinct");
        var entryPoint = text.IndexOf("if ($Mode -eq 'ProbeUnavailable')", StringComparison.Ordinal);
        var guardCall = text.IndexOf("Assert-OptionalProbeTargetIsDistinct", entryPoint, StringComparison.Ordinal);

        Assert.Contains("$ProbeTableName", probeGuard, StringComparison.Ordinal);
        Assert.Contains("$ProbeTagName", probeGuard, StringComparison.Ordinal);
        Assert.Contains("$TableName", probeGuard, StringComparison.Ordinal);
        Assert.Contains("$TagName", probeGuard, StringComparison.Ordinal);
        Assert.Contains("$PlcName", probeGuard, StringComparison.Ordinal);
        Assert.True(guardCall > entryPoint,
            "ProbeUnavailable must reject a target identical to the mandatory drift target before startup.");

        var probeCase = ExtractSwitchCase(text, "ProbeUnavailable", "}" );
        var mcpTool = ExtractTopLevelFunction(text, "Invoke-McpTool");
        Assert.Contains("[switch]$AllowApplicationError", mcpTool, StringComparison.Ordinal);
        Assert.Contains("-AllowApplicationError", probeCase, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ApplyDriftPreplansReconciliationAndVerifiesFinalSnapshot()
    {
        var text = ReadScript();
        var applyCase = ExtractSwitchCase(text, "ApplyDrift", "ProbeUnavailable");
        var firstMutatingApply = applyCase.IndexOf(
            "Invoke-Apply -Operation $originalOperation -SafetyToken $intermediateToken",
            StringComparison.Ordinal);
        var reconciliationPlan = applyCase.IndexOf(
            "New-UpdateTagOperation -Snapshot $snapshot -FlagName $DriftFlagName -Value $currentValue -OperationId 'update-tag-restore-original-flag'",
            StringComparison.Ordinal);

        Assert.True(reconciliationPlan >= 0 && reconciliationPlan < firstMutatingApply,
            "ApplyDrift must prepare reconciliation before its intermediate mutation can be issued.");
        Assert.DoesNotContain("if ($intermediateApplied)", applyCase, StringComparison.Ordinal);
        Assert.Contains("Assert-SnapshotFlagEquals", applyCase, StringComparison.Ordinal);
        Assert.Contains("-ExpectedValue $currentValue", applyCase, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ReadRejectsApplicationErrorsAndComparesThePublicTagRow()
    {
        var text = ReadScript();
        var mcpTool = ExtractTopLevelFunction(text, "Invoke-McpTool");
        var publicComparison = ExtractTopLevelFunction(text, "Assert-PublicTagRowMatchesSnapshot");
        var readCase = ExtractSwitchCase(text, "Read", "PreviewDrift");

        Assert.Contains("$result.isError", mcpTool, StringComparison.Ordinal);
        Assert.Contains("$ToolCall.Result.isError", publicComparison, StringComparison.Ordinal);
        Assert.Contains("$Snapshot", publicComparison, StringComparison.Ordinal);
        Assert.Contains("Invoke-McpTool -Name 'execute_read_batch'", readCase, StringComparison.Ordinal);
        Assert.Contains("operation = 'list_tag_tables'", readCase, StringComparison.Ordinal);
        Assert.Contains("Assert-PublicTagRowMatchesSnapshot", readCase, StringComparison.Ordinal);
        Assert.True(
            readCase.IndexOf("Assert-PublicTagRowMatchesSnapshot", StringComparison.Ordinal)
            > readCase.IndexOf("Invoke-McpTool -Name 'execute_read_batch'", StringComparison.Ordinal),
            "Read must compare the public list_tag_tables row after its call succeeds.");
    }

    private static string ReadScript()
    {
        Assert.True(File.Exists(ScriptPath), $"Expected live harness at {ScriptPath}.");
        return File.ReadAllText(ScriptPath).ReplaceLineEndings("\n");
    }

    private static string ExtractTopLevelFunction(string text, string name)
    {
        var start = text.IndexOf($"function {name} {{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected function '{name}'.");
        var next = text.IndexOf("\nfunction ", start + 1, StringComparison.Ordinal);
        return next >= 0 ? text[start..next] : text[start..];
    }

    private static string ExtractSwitchCase(string text, string name, string nextName)
    {
        var start = text.IndexOf($"'{name}' {{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected '{name}' switch case.");
        if (nextName == "}")
        {
            var finallyStart = text.IndexOf("\n}\nfinally", start, StringComparison.Ordinal);
            Assert.True(finallyStart > start, $"Expected switch closing boundary after '{name}'.");
            return text[start..finallyStart];
        }
        var end = text.IndexOf($"'{nextName}' {{", start + 1, StringComparison.Ordinal);
        Assert.True(end > start, $"Expected '{nextName}' after '{name}'.");
        return text[start..end];
    }

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
