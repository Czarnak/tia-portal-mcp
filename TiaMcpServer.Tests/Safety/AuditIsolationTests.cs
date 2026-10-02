using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Safety;

/// <summary>Tool calls use the injected audit sink, including dry runs and rejections.</summary>
public class AuditIsolationTests
{
    private static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TiaMcpServer", "audit");

    private static int CountAuditLines(string directory)
        => Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.jsonl").Sum(file => File.ReadAllLines(file).Length)
            : 0;

    [Fact]
    public async Task ProjectWriteTool_DryRunAndApplyWriteOnlyToTheInjectedDirectory()
    {
        var before = CountAuditLines(DefaultDirectory);
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
        var execution = LifecycleTestCalls.Execution(client, audit);
        using var fixture = new LifecycleProtocolFixture();
        var path = fixture.DestinationPath;

        var preview = await ProjectWriteTools.OpenProject(client, execution, path, dryRun: true);
        Assert.Equal("preview", LifecycleTestCalls.Document(preview).GetProperty("phase").GetString());
        Assert.Equal(1, LifecycleTestCalls.AuditCount(audit));
        var applied = await ProjectWriteTools.OpenProject(client, execution, path);
        Assert.True(LifecycleTestCalls.Document(applied).GetProperty("success").GetBoolean());

        Assert.Equal(2, LifecycleTestCalls.AuditCount(audit));
        Assert.Equal(before, CountAuditLines(DefaultDirectory));
    }

    [Fact]
    public async Task BindingRejectedSave_RecordsOneRejectedCallOnlyInTheInjectedDirectory()
    {
        var before = CountAuditLines(DefaultDirectory);
        using var audit = new TempAuditDirectory();
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null),
            workerExecutablePath: "worker-must-not-start.exe",
            accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));

        var result = await ProjectWriteTools.SaveProject(client,
            LifecycleTestCalls.Execution(client, audit));

        LifecycleTestCalls.Rejected(result, WorkerFailureCategories.BindingConflict);
        Assert.Equal(1, LifecycleTestCalls.AuditCount(audit));
        Assert.Equal(before, CountAuditLines(DefaultDirectory));
    }

    [Fact]
    public async Task RejectedSaveProjectAs_RebindFalse_RecordsOneRejectedCallOnlyInTheInjectedDirectory()
    {
        var before = CountAuditLines(DefaultDirectory);
        using var audit = new TempAuditDirectory();
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null),
            workerExecutablePath: "worker-must-not-start.exe",
            accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));

        var result = await ProjectWriteTools.SaveProjectAs(client,
            LifecycleTestCalls.Execution(client, audit),
            targetDirectory: @"C:\Target", targetName: "Copy", rebind: false);

        LifecycleTestCalls.Rejected(result, WorkerFailureCategories.ValidationError);
        Assert.Equal(1, LifecycleTestCalls.AuditCount(audit));
        Assert.Equal(before, CountAuditLines(DefaultDirectory));
    }
}
