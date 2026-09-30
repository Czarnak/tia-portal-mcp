using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectLifecycleDelegationTests
{
    [Fact]
    public async Task SaveProject_RegisteredToolRequiresVerifiedBindingBeforeTransport()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding,
            workerExecutablePath: "worker-must-not-start.exe",
            accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
        var before = binding.CaptureSnapshot();

        var result = await ProjectWriteTools.SaveProject(client,
            LifecycleTestCalls.Execution(client, audit), new UserConfirmationOptions(false));

        LifecycleTestCalls.Rejected(result, WorkerFailureCategories.BindingConflict);
        Assert.True(before.SameBinding(binding.CaptureSnapshot()));
        Assert.Equal(1, LifecycleTestCalls.AuditCount(audit));
    }
}
