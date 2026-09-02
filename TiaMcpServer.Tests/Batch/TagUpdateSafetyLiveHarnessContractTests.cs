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
        Assert.Matches(new Regex(@"\[switch\]\s*\$AllowApply"), text);
        Assert.Contains("$DriftFlagName", text, StringComparison.Ordinal);
        Assert.Contains("read_update_tag_safety_snapshot", text, StringComparison.Ordinal);
        Assert.Contains("state_changed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_InternalSafetyReadCarriesObservedSessionIdentity()
    {
        var text = ReadScript();
        Assert.Contains("get_project_status", text, StringComparison.Ordinal);
        Assert.Contains("sessionIdentity", text, StringComparison.Ordinal);
        Assert.Contains("expectedSessionIdentity", text, StringComparison.Ordinal);
        Assert.Contains("read_update_tag_safety_snapshot", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_OptionalUnavailableProbeUsesSeparateTargetInputs()
    {
        var text = ReadScript();
        Assert.Contains("$ProbeTagName", text, StringComparison.Ordinal);
        Assert.Contains("$ProbeFlagName", text, StringComparison.Ordinal);
        Assert.Contains("'ProbeUnavailable'", text, StringComparison.Ordinal);
    }

    private static string ReadScript()
    {
        Assert.True(File.Exists(ScriptPath), $"Expected live harness at {ScriptPath}.");
        return File.ReadAllText(ScriptPath).ReplaceLineEndings("\n");
    }

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
