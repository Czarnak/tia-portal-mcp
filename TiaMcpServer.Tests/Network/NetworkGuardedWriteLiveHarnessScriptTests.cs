using System.Text.RegularExpressions;
using Xunit;
namespace TiaMcpServer.Tests.Network;
public sealed class NetworkGuardedWriteLiveHarnessScriptTests
{
    private static string Source
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.slnx")))
                {
                    var path = Path.Combine(directory.FullName, "scripts", "live-test-network-guarded-write.ps1");
                    return File.Exists(path) ? File.ReadAllText(path) : "";
                }
            throw new InvalidOperationException("Repository root not found.");
        }
    }
    [Fact]
    public void InventoryAndPreview_NeverMutate()
    {
        var script = Source;
        Assert.Contains("$Mode = 'Inventory'", script);
        Assert.Contains("dryRun = $true", script);
        Assert.Contains("'Inventory' { Invoke-Inventory }", script);
        Assert.Contains("'Preview' { Invoke-Preview }", script);
        Assert.Contains("Unexpected server elicitation", script);
    }
    [Fact]
    public void Apply_RequiresExactTargetAuthorization()
    {
        var script = Source;
        Assert.Contains("if (-not $AllowMutation)", script);
        Assert.Contains("$Acknowledgement -cne", script);
        Assert.Contains("$AuthorizedProjectPath -cne $ProjectPath", script);
        Assert.Contains("$AuthorizedFixtureSha256 -ine", script);
        Assert.Contains("Assert-FrozenCandidate", script);
        Assert.Contains("bind_project", script);
        Assert.Contains("$AccessMode = 'read-write'", script);
    }
    [Fact]
    public void PublicCalls_UseDryRunAndNoTokens()
    {
        var script = Source;
        Assert.Contains("dryRun = $false", script);
        Assert.DoesNotContain("safetyToken", script);
        Assert.DoesNotMatch(new Regex(@"(?i)\bconfirm\s*="), script);
        Assert.Contains("earlierOperationFailed", script);
        Assert.Contains("immediateChecks", script);
        Assert.Contains("ExpectedItemStatuses", script);
    }
    [Fact]
    public void Restoration_UsesFreshIdentityInspection()
    {
        var script = Source;
        Assert.Contains("inspect_network_object", script);
        Assert.Contains("Read-FixtureIdentities", script);
        Assert.Contains("RestoreOperations", script);
        Assert.Contains("RestorationExpected", script);
        Assert.Contains("No automatic restoration after a failed or uncertain write", script);
        Assert.DoesNotContain("-Name 'save_project'", script);
        Assert.DoesNotContain("-Name 'close_project'", script);
        Assert.DoesNotContain("-Name 'compile_check'", script);
    }
}
