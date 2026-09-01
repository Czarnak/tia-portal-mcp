using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

/// <summary>
/// Static, execution-free contract tests for the separately authorized live TIA Portal V21
/// acceptance harness. These tests inspect source only; they never launch TIA Portal, the MCP
/// host, or the script itself.
/// </summary>
public class WriteToolMetadataLiveHarnessContractTests
{
    private static readonly string ScriptPath = Path.GetFullPath(
        Path.Combine(GetRepositoryRoot(), "scripts", "live-test-write-tool-metadata.ps1"));

    [Fact]
    public void Script_Exists()
    {
        Assert.True(File.Exists(ScriptPath), $"Expected the live harness at '{ScriptPath}'.");
    }

    [Fact]
    public void Script_LaunchesTheRealMcpHostTwice_AndSpeaksInitializeListCallProtocol()
    {
        var text = ReadScript();

        Assert.Matches(new Regex(@"--read-only"), text);
        Assert.Matches(new Regex(@"--read-write"), text);
        Assert.Contains("'initialize'", text, StringComparison.Ordinal);
        Assert.Contains("notifications/initialized", text, StringComparison.Ordinal);
        Assert.Contains("tools/list", text, StringComparison.Ordinal);
        Assert.Contains("tools/call", text, StringComparison.Ordinal);
        Assert.Contains("TiaMcpServer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OpennessWorker.exe", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_RequiresPowerShell7AndMandatoryProjectAndReportPaths()
    {
        var text = ReadScript();

        Assert.Matches(new Regex(@"^\s*#Requires\s+-Version\s+7(\.\d+)?\s*$", RegexOptions.Multiline), text);
        Assert.Matches(new Regex(@"\[Parameter\(Mandatory\)\]\s*\[string\]\s*\$ProjectPath"), text);
        Assert.Matches(new Regex(@"\[Parameter\(Mandatory\)\]\s*\[string\]\s*\$ReportPath"), text);
    }

    [Fact]
    public void Script_NeverIssuesConfirmingApply()
    {
        var text = ReadScript();

        Assert.DoesNotMatch(new Regex(@"confirm\s*=\s*\$true", RegexOptions.IgnoreCase), text);
    }

    [Fact]
    public void Script_CallsOnlyBenignProjectStatusTool()
    {
        var text = ReadScript();

        var toolsCallRequests = Regex.Matches(
            text,
            "Invoke-McpRequest\\s+-Method\\s+['\\\"]tools/call['\\\"]",
            RegexOptions.IgnoreCase);
        Assert.Single(toolsCallRequests);

        Assert.Matches(
            new Regex(@"Invoke-McpToolCall\s+-Name\s+'get_project_status'", RegexOptions.IgnoreCase),
            text);
        Assert.True(
            Regex.Matches(text, @"Invoke-McpToolCall\s+-Name\s+'([^']+)'", RegexOptions.IgnoreCase)
                .Cast<Match>()
                .All(match => string.Equals(match.Groups[1].Value, "get_project_status", StringComparison.OrdinalIgnoreCase)),
            "The harness may call only get_project_status through tools/call.");
    }

    [Fact]
    public void Script_FailsClosedOnAnyApprovedWriteAnnotationMismatch()
    {
        var text = ReadScript();

        Assert.Contains("$script:ExpectedWriteToolAnnotations", text, StringComparison.Ordinal);
        Assert.Contains("function Assert-ToolAnnotationEvidence", text, StringComparison.Ordinal);

        foreach (var (toolName, readOnlyHint, destructiveHint, openWorldHint) in new[]
                 {
                     ("preview_write_batch", true, false, false),
                     ("apply_write_batch", false, true, false),
                     ("open_project", false, true, false),
                     ("create_project", false, true, false),
                     ("save_project", false, true, false),
                     ("save_project_as", false, true, false),
                     ("archive_project", false, true, false),
                     ("close_project", false, true, false),
                 })
        {
            var expected = $@"(?s)'{toolName}'\s*=\s*@\{{.*?readOnlyHint\s*=\s*\${readOnlyHint.ToString().ToLowerInvariant()}.*?destructiveHint\s*=\s*\${destructiveHint.ToString().ToLowerInvariant()}.*?openWorldHint\s*=\s*\${openWorldHint.ToString().ToLowerInvariant()}";
            Assert.Matches(new Regex(expected), text);
        }
    }

    [Fact]
    public void StaticSafetyGuard_IsCaseInsensitiveAndCountsEveryToolsCallRequest()
    {
        var confirmingWriteGuard = ReadThisTestMethod("Script_NeverIssuesConfirmingApply");
        Assert.Contains("RegexOptions.IgnoreCase", confirmingWriteGuard, StringComparison.Ordinal);

        var benignCallGuard = ReadThisTestMethod("Script_CallsOnlyBenignProjectStatusTool");
        Assert.Contains("Invoke-McpRequest", benignCallGuard, StringComparison.Ordinal);
        Assert.Contains("Assert.Single", benignCallGuard, StringComparison.Ordinal);
        Assert.Contains("RegexOptions.IgnoreCase", benignCallGuard, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_RecordsTheExactFourAndFourteenToolSurfaces()
    {
        var text = ReadScript();

        Assert.Matches(new Regex(@"\$script:ExpectedReadOnlyToolNames\s*=\s*@\s*\("), text);
        Assert.Matches(new Regex(@"\$script:ExpectedReadWriteToolNames\s*=\s*@\s*\("), text);
        Assert.Matches(new Regex(@"\$script:ExpectedReadOnlyToolCount\s*=\s*4"), text);
        Assert.Matches(new Regex(@"\$script:ExpectedReadWriteToolCount\s*=\s*14"), text);

        foreach (var toolName in new[]
                 {
                     "get_project_status", "browse_project_tree", "execute_read_batch", "network_read",
                 })
        {
            Assert.Contains($"'{toolName}'", text, StringComparison.Ordinal);
        }

        foreach (var toolName in new[]
                 {
                     "get_project_status", "browse_project_tree", "execute_read_batch", "network_read",
                     "compile_check", "open_project", "create_project", "save_project", "save_project_as",
                     "archive_project", "close_project", "preview_write_batch", "apply_write_batch", "network_write",
                 })
        {
            Assert.Contains($"'{toolName}'", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Script_UsesTheVersionedAcceptanceReportDirectory()
    {
        var text = ReadScript();

        Assert.Contains("docs", text, StringComparison.Ordinal);
        Assert.Contains("superpowers", text, StringComparison.Ordinal);
        Assert.Contains("acceptance", text, StringComparison.Ordinal);
        Assert.Contains("reports", text, StringComparison.Ordinal);
        Assert.Contains("2026-09-01-pr1-explicit-mcp-tool-annotations-live.md", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_FailsClosedOnInvalidProjectStatusEvidence_AndRecordsOnlySanitizedSummary()
    {
        var text = ReadScript();
        var parser = ReadScriptFunction("Get-ProjectStatusEvidence");

        Assert.Matches(
            new Regex(@"Get-RequiredBooleanProperty\s+-Object\s+\$statusEnvelope\s+-Name\s+'success'\s+-ExpectedValue\s+\$true"),
            parser);
        Assert.Matches(
            new Regex(@"Get-RequiredBooleanProperty\s+-Object\s+\$statusPayload\s+-Name\s+'success'\s+-ExpectedValue\s+\$true"),
            parser);
        Assert.Matches(
            new Regex(@"Get-RequiredNormalizedPathProperty\s+-Object\s+\$statusPayload\s+-Name\s+'projectPath'\s+-ExpectedPath\s+\$resolvedExpectedProjectPath"),
            parser);
        Assert.Matches(
            new Regex(@"\$project\s*=\s*Get-PropertyValue\s+-Object\s+\$statusPayload\s+-Name\s+'project'"),
            parser);
        Assert.Contains("$null -eq $project", parser, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"Get-RequiredBooleanProperty\s+-Object\s+\$project\s+-Name\s+'isOpen'\s+-ExpectedValue\s+\$true"),
            parser);
        Assert.Matches(
            new Regex(@"Get-RequiredNormalizedPathProperty\s+-Object\s+\$project\s+-Name\s+'path'\s+-ExpectedPath\s+\$resolvedExpectedProjectPath"),
            parser);
        Assert.Matches(
            new Regex(@"Get-RequiredNormalizedPathProperty\s+-Object\s+\$sessionIdentity\s+-Name\s+'projectPath'\s+-ExpectedPath\s+\$resolvedExpectedProjectPath"),
            parser);
        Assert.Contains("throw", parser, StringComparison.Ordinal);
        Assert.DoesNotContain("metadata", parser, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("projectStatusSummary", text, StringComparison.Ordinal);
        Assert.DoesNotContain("projectStatusResult", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ConvertFrom-Json -Depth 80)),", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoOrdinaryTestInvokesTheLiveHarnessScript()
    {
        var testDirectory = Path.Combine(GetRepositoryRoot(), "TiaMcpServer.Tests");
        var thisFile = Path.Combine(testDirectory, "Tools", "WriteToolMetadataLiveHarnessContractTests.cs");

        var offendingFiles = Directory
            .EnumerateFiles(testDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(thisFile), StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains(
                "live-test-write-tool-metadata.ps1", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offendingFiles);
    }

    private static string ReadScript()
    {
        Assert.True(File.Exists(ScriptPath), $"Expected the live harness at '{ScriptPath}'.");
        return File.ReadAllText(ScriptPath);
    }

    private static string ReadThisTestMethod(string methodName)
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "TiaMcpServer.Tests",
            "Tools",
            "WriteToolMetadataLiveHarnessContractTests.cs"));
        var methodStart = source.IndexOf($"public void {methodName}()", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Expected test method '{methodName}'.");

        var nextFact = source.IndexOf("\n    [Fact]", methodStart + 1, StringComparison.Ordinal);
        return nextFact >= 0 ? source[methodStart..nextFact] : source[methodStart..];
    }

    private static string ReadScriptFunction(string functionName)
    {
        var text = ReadScript();
        var functionStart = text.IndexOf($"function {functionName}", StringComparison.Ordinal);
        Assert.True(functionStart >= 0, $"Expected PowerShell function '{functionName}'.");

        var nextFunction = text.IndexOf("\nfunction ", functionStart + 1, StringComparison.Ordinal);
        return nextFunction >= 0 ? text[functionStart..nextFunction] : text[functionStart..];
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."));
    }
}
