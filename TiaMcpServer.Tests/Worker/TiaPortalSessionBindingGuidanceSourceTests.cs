using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class TiaPortalSessionBindingGuidanceSourceTests
{
    [Fact]
    public void GuidanceStrings_NameBindProject()
    {
        // Source guard for user guidance, alongside source-linked session behavior tests.
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(repositoryRoot,
            "TiaMcpServer.OpennessWorker", "Openness", "TiaPortalSession.cs"));
        var methodStart = source.IndexOf("public void ValidateExpectedSessionIdentity(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "ValidateExpectedSessionIdentity method was not found.");
        var missingIdentityEnd = source.IndexOf("var current =", methodStart, StringComparison.Ordinal);
        Assert.True(missingIdentityEnd > methodStart, "Missing-identity branch was not found.");
        var guidance = source.Substring(methodStart, missingIdentityEnd - methodStart);

        Assert.Contains("verified project binding", guidance, StringComparison.Ordinal);
        Assert.Contains("bind_project", guidance, StringComparison.Ordinal);
        Assert.Contains("open_project", guidance, StringComparison.Ordinal);
        Assert.True(guidance.IndexOf("bind_project", StringComparison.Ordinal)
            < guidance.IndexOf("open_project", StringComparison.Ordinal));
        Assert.Contains("create_project", guidance, StringComparison.Ordinal);
        Assert.Contains("--project", guidance, StringComparison.Ordinal);
        Assert.DoesNotContain("get_project_status", guidance, StringComparison.Ordinal);

        foreach (var relativePath in new[] { "TiaMcpServer.OpennessWorker/Program.cs", "TiaMcpServer.FakeWorker/Program.cs" })
        {
            var workerSource = File.ReadAllText(Path.Combine(repositoryRoot, relativePath));
            Assert.Contains("ProjectOpenPolicy.NoProjectOpenMessage(", workerSource, StringComparison.Ordinal);
            Assert.DoesNotContain("No project is open in TIA Portal. Open the intended project manually and retry.", workerSource);
            Assert.DoesNotContain("Provide a projectPath argument or open a project in TIA Portal.", workerSource);
        }

        var lifecycle = File.ReadAllText(Path.Combine(repositoryRoot,
            "TiaMcpServer.OpennessWorker", "Openness", "ProjectLifecycleService.cs"));
        Assert.DoesNotContain("Provide a projectPath argument or open a project in TIA Portal.", lifecycle);
        Assert.Contains("session.RequireStandaloneOwner().Project", lifecycle);
        Assert.Contains("ProjectOpenPolicy.NoProjectOpenMessage(", source, StringComparison.Ordinal);

        var clientSource = File.ReadAllText(Path.Combine(repositoryRoot, "TiaMcpServer", "Worker", "OpennessWorkerClient.cs"));
        var refusalStart = clientSource.IndexOf("An invalidated source can be re-grounded", StringComparison.Ordinal);
        Assert.True(refusalStart >= 0);
        var refusalEnd = clientSource.IndexOf("));", refusalStart, StringComparison.Ordinal);
        Assert.True(refusalEnd > refusalStart);
        var refusal = clientSource.Substring(refusalStart, refusalEnd - refusalStart);
        Assert.Contains("bind_project", refusal);
        Assert.True(refusal.IndexOf("bind_project", StringComparison.Ordinal)
            < refusal.IndexOf("open_project", StringComparison.Ordinal));

        var gateCommentStart = clientSource.IndexOf("Fail-closed gate for every project-mutating preview/apply path.", StringComparison.Ordinal);
        var gateCommentEnd = clientSource.IndexOf("public async Task<WorkerCallResult> RequireVerifiedWriteBindingAsync", gateCommentStart, StringComparison.Ordinal);
        Assert.True(gateCommentStart >= 0 && gateCommentEnd > gateCommentStart);
        var gateComment = clientSource.Substring(gateCommentStart, gateCommentEnd - gateCommentStart);
        Assert.Contains("bind_project", gateComment);
        Assert.True(gateComment.IndexOf("bind_project", StringComparison.Ordinal)
            < gateComment.IndexOf("open_project", StringComparison.Ordinal));
    }
}
