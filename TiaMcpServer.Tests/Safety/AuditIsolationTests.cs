using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.TestSupport;
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
        using var client = FakeWorkerClients.Create(McpAccessMode.Full, binding: binding);
        var execution = LifecycleTestCalls.Execution(client, audit);
        using var fixture = new LifecycleProtocolFixture();
        var path = fixture.DestinationPath;

        var preview = await ProjectWriteTools.OpenProject(client, execution, path, dryRun: true);
        Assert.Equal("preview", CanonicalToolDocument.Read(preview).GetProperty("phase").GetString());
        Assert.Equal(1, LifecycleTestCalls.AuditCount(audit));
        var applied = await ProjectWriteTools.OpenProject(client, execution, path);
        Assert.True(CanonicalToolDocument.Read(applied).GetProperty("success").GetBoolean());

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
