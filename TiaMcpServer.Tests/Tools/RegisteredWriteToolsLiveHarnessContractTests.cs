using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

public sealed class RegisteredWriteToolsLiveHarnessContractTests
{
    private static readonly string ScriptPath = Path.GetFullPath(
        Path.Combine(GetRepositoryRoot(), "scripts", "live-test-write-safety-pr2-registered-tools.ps1"));

    [Fact]
    public void Script_RequiresPowerShell7_AndRealMcpProtocol()
    {
        var text = File.ReadAllText(ScriptPath);
        Assert.Matches(new Regex(@"^\s*#Requires\s+-Version\s+7(\.\d+)?\s*$", RegexOptions.Multiline), text);
        Assert.Contains("'initialize'", text, StringComparison.Ordinal);
        Assert.Contains("notifications/initialized", text, StringComparison.Ordinal);
        Assert.Contains("'tools/list'", text, StringComparison.Ordinal);
        Assert.Contains("'tools/call'", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ListsAllRegisteredWriteTools_ButNeverConstructsAnApplyToolCall()
    {
        var text = File.ReadAllText(ScriptPath);
        Assert.Contains("apply_write_batch", text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Invoke-PreviewToolCall -ToolName 'apply_write_batch'",
            text,
            StringComparison.Ordinal);
        var calls = ExtractPreviewToolCalls(text);

        Assert.Equal(
            new[] { "execute_read_batch", "preview_write_batch", "save_project" },
            calls.Select(call => call.Name).ToArray());
        Assert.All(
            calls,
            call =>
            {
                Assert.DoesNotContain("confirm = $true", call.ArgumentsBlock, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("safetyToken", call.ArgumentsBlock, StringComparison.OrdinalIgnoreCase);
            });
        Assert.DoesNotContain("Read-Host", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_RequiresProjectAndTypePath_AndStartsTheHostWithStartupBinding()
    {
        var text = File.ReadAllText(ScriptPath);
        Assert.Matches(new Regex(@"\[Parameter\(Mandatory\)\]\s*\[string\]\s*\$ProjectPath"), text);
        Assert.Matches(new Regex(@"\[Parameter\(Mandatory\)\]\s*\[string\]\s*\$TypePath"), text);
        Assert.Contains("--project", text, StringComparison.Ordinal);
        Assert.Contains("preview_write_batch", text, StringComparison.Ordinal);
        Assert.Contains("save_project", text, StringComparison.Ordinal);
    }

    private static (string Name, string ArgumentsBlock)[] ExtractPreviewToolCalls(string text)
        => Regex.Matches(
                text,
                @"Invoke-PreviewToolCall\s+-ToolName\s+'(?<name>[^']+)'\s+-Arguments\s+@\{(?<args>.*?)\}",
                RegexOptions.Singleline)
            .Select(match => (match.Groups["name"].Value, match.Groups["args"].Value))
            .ToArray();

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
