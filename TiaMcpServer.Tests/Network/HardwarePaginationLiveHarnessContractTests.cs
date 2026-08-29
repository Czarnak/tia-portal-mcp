using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Static, execution-free contract tests for the separately authorized live-TIA hardware
/// pagination harness. The tests inspect the script but never execute it.
/// </summary>
public class HardwarePaginationLiveHarnessContractTests
{
    private static readonly string ScriptPath = Path.GetFullPath(
        Path.Combine(GetRepositoryRoot(), "scripts", "live-test-hardware-pagination.ps1"));

    [Fact]
    public void Script_ExistsAndRequiresPowerShell7StrictMode()
    {
        var text = ReadScript();

        Assert.Matches(new Regex(@"^\s*#Requires\s+-Version\s+7(\.\d+)?\s*$", RegexOptions.Multiline), text);
        Assert.Contains("Set-StrictMode -Version Latest", text, StringComparison.Ordinal);
        Assert.Contains("$ErrorActionPreference = 'Stop'", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_IsReadOnlyAndUsesOnlyThePublicHardwareRead()
    {
        var text = ReadScript();

        Assert.Contains("network_read", text, StringComparison.Ordinal);
        Assert.Contains("read_hardware_config", text, StringComparison.Ordinal);
        Assert.DoesNotContain("network_write", text, StringComparison.Ordinal);
        Assert.DoesNotContain("preview_write_batch", text, StringComparison.Ordinal);
        Assert.DoesNotContain("apply_write_batch", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"confirm\s*="), text);
        Assert.DoesNotContain("read_hardware_page_candidates", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_AcceptsTheBoundQueryAndPageSizeInputs()
    {
        var text = ReadScript();

        Assert.Matches(new Regex(@"\[Parameter\(Mandatory\)\]\s*\[string\]\s*\$ProjectPath"), text);
        Assert.Contains("$DeviceName", text, StringComparison.Ordinal);
        Assert.Contains("$PlcName", text, StringComparison.Ordinal);
        Assert.Contains("$IncludeIoDetails", text, StringComparison.Ordinal);
        Assert.Contains("$IncludeTagMatches", text, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"\[ValidateRange\(1,\s*200\)\]\s*\[int\]\s*\$PageSize"), text);
        Assert.Contains("-IncludeTagMatches requires -IncludeIoDetails", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_FollowsNextCursorWithoutChangingBoundQueryFields()
    {
        var text = ReadScript();

        Assert.Contains("while ($null -ne $nextCursor)", text, StringComparison.Ordinal);
        Assert.Contains("$readOperation.cursor = $nextCursor", text, StringComparison.Ordinal);
        Assert.Contains("$readOperation.pageSize = $PageSize", text, StringComparison.Ordinal);
        Assert.Contains("Assert-BoundQueryUnchanged", text, StringComparison.Ordinal);
        Assert.Contains("deviceName", text, StringComparison.Ordinal);
        Assert.Contains("plcName", text, StringComparison.Ordinal);
        Assert.Contains("includeIoDetails", text, StringComparison.Ordinal);
        Assert.Contains("includeTagMatches", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_EnforcesItemLimitStableTotalsAndExactOffsets()
    {
        var text = ReadScript();

        Assert.Contains("$script:ItemCharacterLimit = 60000", text, StringComparison.Ordinal);
        Assert.Contains("Assert-CanonicalItemWithinLimit", text, StringComparison.Ordinal);
        Assert.Contains("Assert-StableTotals", text, StringComparison.Ordinal);
        Assert.Contains("Assert-CombinedPageOrderAndSize", text, StringComparison.Ordinal);
        Assert.Contains("Assert-PageOffsets", text, StringComparison.Ordinal);
        Assert.Contains("deviceStartOffset", text, StringComparison.Ordinal);
        Assert.Contains("deviceEndOffset", text, StringComparison.Ordinal);
        Assert.Contains("subnetStartOffset", text, StringComparison.Ordinal);
        Assert.Contains("subnetEndOffset", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_RecordsTimingSeparatelyFromCorrectnessEvidence()
    {
        var text = ReadScript();

        Assert.Contains("correctness.json", text, StringComparison.Ordinal);
        Assert.Contains("timing.json", text, StringComparison.Ordinal);
        Assert.Contains("Stopwatch", text, StringComparison.Ordinal);
        Assert.Contains("elapsedMilliseconds", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_StopsWithAnArtifactOnCursorFailureOrOmission()
    {
        var text = ReadScript();

        Assert.Contains("failure.json", text, StringComparison.Ordinal);
        Assert.Contains("Write-FailureArtifact", text, StringComparison.Ordinal);
        Assert.Contains("omitted", text, StringComparison.Ordinal);
        foreach (var category in new[]
                 {
                     "invalid_cursor",
                     "cursor_filter_mismatch",
                     "cursor_binding_mismatch",
                     "cursor_snapshot_mismatch",
                     "cursor_out_of_range"
                 })
        {
            Assert.Contains(category, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Script_LaunchesTheRealReadOnlyMcpHostAndSpeaksNdjson()
    {
        var text = ReadScript();

        Assert.Contains("TiaMcpServer.dll", text, StringComparison.Ordinal);
        Assert.Contains("--access-mode", text, StringComparison.Ordinal);
        Assert.Contains("read-only", text, StringComparison.Ordinal);
        Assert.Contains("'initialize'", text, StringComparison.Ordinal);
        Assert.Contains("notifications/initialized", text, StringComparison.Ordinal);
        Assert.Contains("'tools/list'", text, StringComparison.Ordinal);
        Assert.Contains("'tools/call'", text, StringComparison.Ordinal);
        Assert.Contains("jsonrpc", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OpennessWorker.exe", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoOrdinaryTestInvokesTheHardwarePaginationLiveHarness()
    {
        var testDirectory = Path.Combine(GetRepositoryRoot(), "TiaMcpServer.Tests");
        var thisFile = Path.Combine(testDirectory, "Network", "HardwarePaginationLiveHarnessContractTests.cs");

        var offendingFiles = Directory
            .EnumerateFiles(testDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !string.Equals(
                Path.GetFullPath(path),
                Path.GetFullPath(thisFile),
                StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains(
                "live-test-hardware-pagination.ps1",
                StringComparison.Ordinal))
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
