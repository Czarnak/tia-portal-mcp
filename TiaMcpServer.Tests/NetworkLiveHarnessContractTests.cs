using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Static, execution-free contract tests for <c>scripts/live-test-network-phase2.ps1</c> — the
/// separately authorized live-TIA MCP-protocol acceptance harness for the Network Operations
/// Phase 2 JSON contract (see <c>docs/NETWORK_OPERATIONS_ROADMAP.md</c> and the Global
/// Constraints of <c>docs/superpowers/plans/2026-08-02-network-operations-phase2-json-contract.md</c>).
///
/// <para>
/// None of these tests execute the script. It requires a live TIA Portal V21 attachment and a
/// separate authorization gate, so every assertion here reads the script's own source text and
/// proves invariants a reviewer can check without running it: PowerShell 7 is required, the
/// default mode never mutates, Preview can never reach a confirming apply call, Apply is gated
/// behind an explicit switch plus enough identity to target one exact node, the harness speaks
/// the real MCP protocol rather than direct worker IPC, and no ordinary test in this project
/// invokes the script.
/// </para>
/// </summary>
public class NetworkLiveHarnessContractTests
{
    private static readonly string ScriptPath = Path.GetFullPath(
        Path.Combine(GetRepositoryRoot(), "scripts", "live-test-network-phase2.ps1"));

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
    public void Script_DefaultModeIsReadOnly()
    {
        var text = ReadScript();
        Assert.Matches(
            new Regex(@"\[ValidateSet\(\s*'Read'\s*,\s*'Preview'\s*,\s*'Apply'\s*\)\]"), text);
        Assert.Matches(new Regex(@"\[string\]\s*\$Mode\s*=\s*'Read'"), text);
    }

    [Fact]
    public void Script_PreviewCaseNeverReachesAConfirmingApplyCall()
    {
        var text = ReadScript();

        // Exactly one confirm:$true call site must exist anywhere in the script (Apply must be
        // reachable at all), and it must live inside Invoke-NetworkWriteApply.
        var confirmTrueSites = Regex.Matches(text, @"confirm\s*=\s*\$true");
        Assert.True(confirmTrueSites.Count == 1,
            $"Expected exactly one confirm:$true call site (in Invoke-NetworkWriteApply); found {confirmTrueSites.Count}.");

        var functionStart = text.IndexOf("function Invoke-NetworkWriteApply", StringComparison.Ordinal);
        Assert.True(functionStart >= 0, "Expected a function named Invoke-NetworkWriteApply.");

        var nextFunctionStart = Regex.Match(text[(functionStart + 1)..], @"^function\s+\S+", RegexOptions.Multiline);
        var functionEnd = nextFunctionStart.Success
            ? functionStart + 1 + nextFunctionStart.Index
            : text.Length;

        Assert.InRange(confirmTrueSites[0].Index, functionStart, functionEnd);

        // The literal 'Preview' switch case block (up to the next case label) must never contain
        // a confirming call: Preview is non-mutating by construction, not just by convention.
        var previewCaseStart = text.IndexOf("'Preview' {", StringComparison.Ordinal);
        Assert.True(previewCaseStart >= 0, "Expected a 'Preview' { ... } case in the mode dispatch.");
        var applyCaseStart = text.IndexOf("'Apply' {", StringComparison.Ordinal);
        Assert.True(applyCaseStart > previewCaseStart, "Expected an 'Apply' { ... } case after 'Preview'.");

        var previewCaseText = text[previewCaseStart..applyCaseStart];
        Assert.DoesNotMatch(new Regex(@"confirm\s*=\s*\$true"), previewCaseText);
        Assert.Contains("Invoke-NetworkWritePreview", previewCaseText, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ApplyRequiresAllowApplySwitchPlusProjectDeviceNodeAndSafeValues()
    {
        var text = ReadScript();

        // Project path is mandatory for every mode (Apply necessarily included).
        Assert.Matches(new Regex(@"\[Parameter\(Mandatory\)\]\s*\[string\]\s*\$ProjectPath"), text);

        // deviceName / nodeId / at least one new value are required whenever mode is not Read
        // (i.e. for both Preview and Apply) - enforced at runtime before anything runs.
        Assert.Contains("-DeviceName", text, StringComparison.Ordinal);
        Assert.Contains("-NodeId", text, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"if\s*\(\s*\$Mode\s*-ne\s*'Read'\s*\)"), text);
        Assert.Matches(new Regex(@"-not\s+\$DeviceName"), text);
        Assert.Matches(new Regex(@"-not\s+\$NodeId"), text);
        Assert.Matches(
            new Regex(@"-not\s+\$NewIpAddress\s+-and\s+-not\s+\$NewSubnetMask\s+-and\s+-not\s+\$NewPnDeviceName"),
            text);

        // Apply additionally requires the explicit -AllowApply switch, checked before anything
        // is read or written.
        Assert.Matches(new Regex(@"\[switch\]\s*\$AllowApply"), text);
        Assert.Matches(new Regex(@"if\s*\(\s*\$Mode\s*-eq\s*'Apply'\s*-and\s*-not\s*\$AllowApply\s*\)"), text);
    }

    [Fact]
    public void Script_PrintsSelectedIdentityAndValuesBeforeConfirmingApply()
    {
        var text = ReadScript();

        var identityPrintIndex = Regex.Match(text, @"Write-Host[^\n]*\$DeviceName").Index;
        var confirmationIndex = text.IndexOf("Read-Host", StringComparison.Ordinal);

        Assert.True(identityPrintIndex >= 0, "Expected the script to print the selected deviceName.");
        Assert.True(confirmationIndex >= 0, "Expected an interactive confirmation prompt via Read-Host.");
        Assert.True(
            identityPrintIndex < confirmationIndex,
            "The selected identity/values must be printed BEFORE the interactive confirmation prompt.");
    }

    [Fact]
    public void Script_RequiresInteractiveConfirmationUnlessCiSwitchSupplied()
    {
        var text = ReadScript();

        Assert.Matches(new Regex(@"\[switch\]\s*\$ConfirmApplyForCi"), text);

        // The Read-Host confirmation must be textually nested inside the SAME if/else statement
        // that branches on $ConfirmApplyForCi, and reached only in the branch where it is falsy:
        // find the smallest window from the nearest preceding "if (...$ConfirmApplyForCi...)"
        // through the matching "else" to the Read-Host call itself.
        var readHostIndex = text.IndexOf("Read-Host", StringComparison.Ordinal);
        Assert.True(readHostIndex >= 0, "Expected an interactive confirmation prompt via Read-Host.");

        var precedingText = text[..readHostIndex];
        var ifConfirmIndex = precedingText.LastIndexOf("if ($ConfirmApplyForCi)", StringComparison.Ordinal);
        Assert.True(ifConfirmIndex >= 0, "Expected 'if ($ConfirmApplyForCi) { ... }' immediately guarding the CI bypass path.");

        var elseIndex = precedingText.IndexOf("else", ifConfirmIndex, StringComparison.Ordinal);
        Assert.True(elseIndex >= 0 && elseIndex < readHostIndex, "Expected Read-Host to be reached only in the 'else' branch of the $ConfirmApplyForCi check.");
    }

    [Fact]
    public void Script_LaunchesTheRealMcpHostAndSpeaksInitializeListCallProtocol()
    {
        var text = ReadScript();

        // The real MCP JSON-RPC sequence, not direct worker IPC.
        Assert.Contains("'initialize'", text, StringComparison.Ordinal);
        Assert.Contains("notifications/initialized", text, StringComparison.Ordinal);
        Assert.Contains("tools/call", text, StringComparison.Ordinal);
        Assert.Contains("jsonrpc", text, StringComparison.Ordinal);

        // It must launch the host (TiaMcpServer), never the worker executable directly - that is
        // the whole point of this harness versus scripts/live-test-db.ps1 / live-test-udt.ps1.
        Assert.Contains("TiaMcpServer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OpennessWorker.exe", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ApplyPathPerformsAnExplicitPostReadComparingSelectedAndOtherNodes()
    {
        var text = ReadScript();

        var applyCaseStart = text.IndexOf("'Apply' {", StringComparison.Ordinal);
        Assert.True(applyCaseStart >= 0, "Expected an 'Apply' { ... } case in the mode dispatch.");
        var applyCaseText = text[applyCaseStart..];

        var readCallCount = Regex.Matches(applyCaseText, @"Read-HardwareConfig\b").Count;
        Assert.True(
            readCallCount >= 2,
            $"Expected at least two Read-HardwareConfig calls (before and after) in the Apply case; found {readCallCount}.");

        Assert.Contains("Get-AllNodeSignatures", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_DocumentsDisposableProjectRequirementAndObviousPlaceholders()
    {
        var text = ReadScript();

        Assert.Matches(new Regex(@"disposable", RegexOptions.IgnoreCase), text);
        Assert.Matches(new Regex(@"back(ed)?[- ]?up", RegexOptions.IgnoreCase), text);

        // 192.0.2.0/24 is the IANA TEST-NET-1 documentation range: an unmistakably fake example
        // value that can never be a real customer network.
        Assert.Contains("192.0.2.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoOrdinaryTestInvokesTheLiveHarnessScript()
    {
        var testDirectory = Path.Combine(GetRepositoryRoot(), "TiaMcpServer.Tests");
        var thisFile = Path.Combine(testDirectory, "NetworkLiveHarnessContractTests.cs");

        var offendingFiles = Directory
            .EnumerateFiles(testDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(thisFile), StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains(
                "live-test-network-phase2.ps1", StringComparison.Ordinal))
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
