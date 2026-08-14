using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Static, execution-free contract tests for <c>scripts/live-test-network-io-map.ps1</c> — the
/// separately authorized live-TIA MCP-protocol acceptance harness for the structured I/O map
/// (<c>read_hardware_config</c> with <c>deviceName</c>/<c>plcName</c>/<c>includeIoDetails</c>/
/// <c>includeTagMatches</c>).
///
/// <para>
/// None of these tests execute the script. It requires a live TIA Portal V21 attachment and a
/// separate authorization gate, so every assertion here reads the script's own source text and
/// proves invariants a reviewer can check without running it: PowerShell 7 is required, the
/// harness is read-only by construction (no write tool, no confirm call site), it speaks the
/// real MCP protocol rather than direct worker IPC, it forwards the I/O-map options, and no
/// ordinary test in this project invokes the script.
/// </para>
/// </summary>
public class NetworkIoMapLiveHarnessContractTests
{
    private static readonly string ScriptPath = Path.GetFullPath(
        Path.Combine(GetRepositoryRoot(), "scripts", "live-test-network-io-map.ps1"));

    [Fact]
    public void Script_Exists()
    {
        Assert.True(File.Exists(ScriptPath), $"Expected the live harness at '{ScriptPath}'.");
    }

    [Fact]
    public void Script_RequiresPowerShell7()
    {
        var text = ReadScript();
        Assert.Matches(new Regex(@"^\s*#Requires\s+-Version\s+7(\.\d+)?\s*$", RegexOptions.Multiline), text);
    }

    [Fact]
    public void Script_IsReadOnlyByConstruction()
    {
        var text = ReadScript();

        // No write tool may be invoked and no confirming call may exist anywhere: this harness
        // exists to prove the read-only I/O map, so there is deliberately no write path at all.
        Assert.DoesNotContain("network_write", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"confirm\s*="), text);
        Assert.DoesNotContain("preview_write_batch", text, StringComparison.Ordinal);
        Assert.DoesNotContain("apply_write_batch", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_DrivesReadHardwareConfigWithTheIoMapOptions()
    {
        var text = ReadScript();

        Assert.Contains("network_read", text, StringComparison.Ordinal);
        Assert.Contains("read_hardware_config", text, StringComparison.Ordinal);
        Assert.Contains("includeIoDetails", text, StringComparison.Ordinal);
        Assert.Contains("includeTagMatches", text, StringComparison.Ordinal);
        Assert.Contains("deviceName", text, StringComparison.Ordinal);
        Assert.Contains("plcName", text, StringComparison.Ordinal);

        // Tag matching without I/O details is rejected before anything runs.
        Assert.Contains("-IncludeTagMatches requires -IncludeIoDetails", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_LaunchesTheRealMcpHostAndSpeaksInitializeListCallProtocol()
    {
        var text = ReadScript();

        Assert.Contains("'initialize'", text, StringComparison.Ordinal);
        Assert.Contains("notifications/initialized", text, StringComparison.Ordinal);
        Assert.Contains("tools/call", text, StringComparison.Ordinal);
        Assert.Contains("jsonrpc", text, StringComparison.Ordinal);

        // It must launch the host (TiaMcpServer), never the worker executable directly.
        Assert.Contains("TiaMcpServer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OpennessWorker.exe", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_RequiresAnExplicitProjectPath()
    {
        var text = ReadScript();

        Assert.Matches(new Regex(@"\[Parameter\(Mandatory\)\]\s*\[string\]\s*\$ProjectPath"), text);
    }

    [Fact]
    public void NoOrdinaryTestInvokesTheIoMapLiveHarnessScript()
    {
        var testDirectory = Path.Combine(GetRepositoryRoot(), "TiaMcpServer.Tests");
        var thisFile = Path.Combine(testDirectory, "Network", "NetworkIoMapLiveHarnessContractTests.cs");

        var offendingFiles = Directory
            .EnumerateFiles(testDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(thisFile), StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains(
                "live-test-network-io-map.ps1", StringComparison.Ordinal))
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
