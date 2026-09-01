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

        Assert.DoesNotMatch(new Regex(@"confirm\s*=\s*\$true"), text);
    }

    [Fact]
    public void Script_CallsOnlyBenignProjectStatusTool()
    {
        var text = ReadScript();

        Assert.Matches(new Regex(@"Invoke-McpToolCall\s+-Name\s+'get_project_status'"), text);
        Assert.True(
            Regex.Matches(text, @"Invoke-McpToolCall\s+-Name\s+'([^']+)'")
                .Cast<Match>()
                .All(match => string.Equals(match.Groups[1].Value, "get_project_status", StringComparison.Ordinal)),
            "The harness may call only get_project_status through tools/call.");
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
