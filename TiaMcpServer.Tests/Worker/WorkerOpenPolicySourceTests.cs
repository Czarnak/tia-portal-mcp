using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class WorkerOpenPolicySourceTests
{
    [Fact]
    public void WorkerSource_OnlyOpenAndCreateCallOpenProject()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var service = File.ReadAllText(Path.Combine(root, "TiaMcpServer.OpennessWorker", "Openness", "ProjectLifecycleService.cs"));
        var open = service.IndexOf("public static ProjectLifecycleResultInfo OpenProject(", StringComparison.Ordinal);
        var save = service.IndexOf("public static ProjectLifecycleResultInfo SaveProject(", StringComparison.Ordinal);
        Assert.True(open >= 0 && save > open);
        Assert.DoesNotContain("session.OpenProject(", service.Substring(0, open));
        Assert.DoesNotContain("session.OpenProject(", service.Substring(save));
        Assert.Contains("ProjectOpenPolicy.Decide", service.Substring(save));
    }

    [Fact]
    public void WorkerSource_EnsureRequestedProjectOpen_NeverOpens()
    {
        // The net48 worker is compiled with Siemens stubs but cannot run in this test process.
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "TiaMcpServer.OpennessWorker", "Program.cs"));
        var start = source.IndexOf("private static WorkerResponse? EnsureRequestedProjectOpen(", StringComparison.Ordinal);
        var end = source.IndexOf("private static void ValidateExpectedAfterProjectResolution(", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "Expected worker method boundaries were not found.");
        var body = source.Substring(start, end - start);
        Assert.DoesNotContain("session.OpenProject(", body, StringComparison.Ordinal);
        Assert.Contains("ProjectOpenDecision.RequestedNotOpen", body, StringComparison.Ordinal);
    }
}
