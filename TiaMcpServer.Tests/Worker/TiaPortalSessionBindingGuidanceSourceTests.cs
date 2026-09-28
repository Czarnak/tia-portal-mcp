using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class TiaPortalSessionBindingGuidanceSourceTests
{
    [Fact]
    public void MissingIdentityGuidance_RequiresVerifiedBindingAndDoesNotClaimStatusBinds()
    {
        // Source-contract guard: the net48 session class is not linked into this net10 test assembly.
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(repositoryRoot,
            "TiaMcpServer.OpennessWorker", "Openness", "TiaPortalSession.cs"));
        var methodStart = source.IndexOf("public void ValidateExpectedSessionIdentity(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "ValidateExpectedSessionIdentity method was not found.");
        var missingIdentityEnd = source.IndexOf("var current = GetSessionIdentity();", methodStart, StringComparison.Ordinal);
        Assert.True(missingIdentityEnd > methodStart, "Missing-identity branch was not found.");
        var guidance = source.Substring(methodStart, missingIdentityEnd - methodStart);

        Assert.Contains("verified project binding", guidance, StringComparison.Ordinal);
        Assert.Contains("open_project", guidance, StringComparison.Ordinal);
        Assert.Contains("create_project", guidance, StringComparison.Ordinal);
        Assert.Contains("--project", guidance, StringComparison.Ordinal);
        Assert.DoesNotContain("get_project_status", guidance, StringComparison.Ordinal);
    }
}
