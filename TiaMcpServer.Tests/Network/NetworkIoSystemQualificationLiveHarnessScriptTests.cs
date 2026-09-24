using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace TiaMcpServer.Tests.Network;

// Offline source and AST contracts. Never launches the live harness or a TIA process.
public sealed class NetworkIoSystemQualificationLiveHarnessScriptTests
{
    private static string Root => FindRoot();
    private static string Source => File.ReadAllText(Path.Combine(Root, "scripts/live-test-network-phase5-qualification.ps1"));

    [Fact]
    public void DefaultsToReadOnlyAndRejectsEffectsBeforeProcessLaunch()
    {
        Assert.Contains("[string] $Mode = 'Inventory'", Source);
        Assert.Contains("$Mode -in @('Compile', 'Apply')", Source);
        Assert.Contains("-not $AllowEffectfulQualification", Source);
        Assert.Contains("$ConfirmationPhrase -cne \"QUALIFY FIXTURE A $Mode\"", Source);
        Assert.True(Source.IndexOf("Effectful qualification requires") < Source.IndexOf("function Start-Child"));
    }

    [Theory]
    [InlineData("IsPathFullyQualified($ProjectPath)")]
    [InlineData("$manifest.commit -cne $ExpectedCommit")]
    [InlineData("$manifest.tree -cne $ExpectedTree")]
    [InlineData("$ExpectedManifestSha256")]
    [InlineData("$ExpectedHarnessSha256")]
    [InlineData("Assert-Same $preview.durableIdentity $durableIdentity")]
    [InlineData("Assert-Same $preview.baseline $owner.before")]
    [InlineData("Assert-Same $preview.ownerTarget $owner.ownerTarget")]
    [InlineData("Assert-Same $preview.proposal $proposal")]
    [InlineData("Assert-Same $preview.binding $binding")]
    [InlineData("expectedSessionIdentity = $sessionIdentity")]
    public void HasExplicitFailClosedBindings(string guard) => Assert.Contains(guard, Source);

    [Fact]
    public void RejectsUnqualifiedOwnerUnsupportedOrMultipleFields()
    {
        Assert.Contains("$owner.ownerMatchCount -ne 1", Source);
        Assert.Contains("$owner.ownerIdentityVerified -ne $true", Source);
        Assert.Contains("$owner.ownerTarget.kind -cne 'deviceItem'", Source);
        Assert.Contains("Assert-Keys $proposal @('attributeName', 'expectedValue', 'desiredValue')", Source);
        Assert.Contains("$proposal.attributeName -cnotin $allowed", Source);
        Assert.Contains("$FixtureAlias -ceq 'DP-A'", Source);
        Assert.Contains("$current.writable -ne $true", Source);
        Assert.Contains("Assert-Same $proposal.expectedValue $current.value", Source);
    }

    [Fact]
    public void RecordsIgnoredEvidenceAndUsesOnlyApprovedRoutes()
    {
        Assert.Contains("git -C $script:Root check-ignore", Source);
        Assert.Contains("'network_read'", Source);
        Assert.Contains("'probe_network_object_attributes'", Source);
        Assert.Contains("'probe_io_system_qualification'", Source);
        Assert.Contains("mode = 'inspectOwner'", Source);
        foreach (var forbidden in new[] { "network_write", "save_project", "download", "set_plc_mode", "compile_check" })
            Assert.DoesNotContain(forbidden, Source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("assertedCandidate = Assert-Candidate", Source);
        Assert.Contains("Stop. No automatic retry", Source);
    }

    [Fact]
    public async Task PowerShellParserAcceptsScriptWithoutExecutingIt()
    {
        _ = Source;
        var path = Path.Combine(Root, "scripts/live-test-network-phase5-qualification.ps1").Replace("'", "''");
        var command = $"$e=$null;$t=$null;$a=[System.Management.Automation.Language.Parser]::ParseFile('{path}',[ref]$t,[ref]$e);if($e.Count){{$e|% Message;exit 1}};if($a.ParamBlock.Parameters.Count -lt 10){{exit 2}}";
        var psi = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000)) { process.Kill(true); throw new TimeoutException("Parser timed out."); }
        Assert.True(process.ExitCode == 0, await output + await error);
    }

    [Fact]
    public void PublicNetworkSurfaceContainsNoQualificationOperation()
    {
        foreach (var file in new[] { "NetworkReadTools.cs", "NetworkWriteTools.cs", "NetworkOperationCatalog.cs" })
        {
            var source = File.ReadAllText(Path.Combine(Root, "TiaMcpServer/Network", file));
            Assert.DoesNotContain("probe_io_system_qualification", source);
            Assert.DoesNotContain("IoSystemQualification", source);
        }
    }

    [Theory]
    [InlineData("NetworkReadTools.cs", "1AB4593461C493D68D5A8EE1658628DD85AC688B52E5B623EBDF94CBA7E86B41")]
    [InlineData("NetworkWriteTools.cs", "CD3F095E4CE2DCE2DC7A450B85F0AF639FC00B1BD1A41FEBB1098E0D141D9CED")]
    public void TemporaryQualificationDoesNotChangePublicToolDeclarationsOrSchemas(string file, string expected)
    {
        // Snapshot the existing declarations, including input/output schema types and descriptions.
        var bytes = Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(Root, "TiaMcpServer/Network", file)).Replace("\r\n", "\n"));
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.sln"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
}
