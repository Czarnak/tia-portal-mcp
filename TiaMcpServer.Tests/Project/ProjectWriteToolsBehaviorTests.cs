using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectWriteToolsBehaviorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveProjectAs_RebindFalse_RejectsBeforeWorkerActivity(bool dryRun)
    {
        using var audit = new TempAuditDirectory();
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null),
            workerExecutablePath: "worker-must-not-start.exe",
            accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));

        var result = await ProjectWriteTools.SaveProjectAs(client,
            LifecycleTestCalls.Execution(client, audit),
            targetDirectory: @"C:\Target", targetName: "Copy", rebind: false, dryRun: dryRun);

        LifecycleTestCalls.Rejected(result, WorkerFailureCategories.ValidationError);
        Assert.Equal(1, LifecycleTestCalls.AuditCount(audit));
    }

    [Fact]
    public async Task SaveProjectAs_MissingCopiedPath_ReportsAttemptedFailureAndNoReplayWarning()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "save-as-uncertain-state");
        Directory.CreateDirectory(audit.Path);

        var result = await ProjectWriteTools.SaveProjectAs(client,
            LifecycleTestCalls.Execution(client, audit),
            targetDirectory: audit.Path, targetName: "Copy");
        var document = LifecycleTestCalls.Document(result);

        Assert.True(result.IsError != true, document.GetRawText());
        Assert.False(document.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("error").ValueKind);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed,
            document.GetProperty("result").GetProperty("failure").GetProperty("category").GetString());
        Assert.Contains(document.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains("state", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, LifecycleTestCalls.AuditCount(audit));
    }
}
