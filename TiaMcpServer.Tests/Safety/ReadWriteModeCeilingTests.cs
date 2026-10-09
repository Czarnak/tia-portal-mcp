using System.Reflection;
using System.Text.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tests.TestUtilities;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Safety;

[Collection(RealWorkerProcessCollection.Name)]
public class ReadWriteModeCeilingTests
{
    public static IEnumerable<object[]> RestrictedOperations => new[]
    {
        "start_plc", "stop_plc"
    }.Select(operation => new object[] { operation });

    private static OpennessWorkerClient CreateClient(ProjectSessionBinding binding, string path, McpAccessMode mode)
        => new(binding, workerExecutablePath: path, accessPolicy: new OperationAccessPolicy(mode));

    private static Task<WorkerCallResult> InvokeRaw(OpennessWorkerClient client, string operation)
        => (Task<WorkerCallResult>)typeof(OpennessWorkerClient)
            .GetMethod("InvokeWorkerAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(client, new object[] { new WorkerRequest { Method = operation } })!;

    private static void AssertNoTransport(OpennessWorkerClient client)
        => Assert.Null(typeof(OpennessWorkerClient).GetField("_transport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client));

    [Theory]
    [MemberData(nameof(RestrictedOperations))]
    public async Task ReadWrite_DeniesRetiredOnlineControlBeforeTransport(string operation)
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding, "worker-must-not-start.exe", McpAccessMode.ReadWrite);
        var result = await InvokeRaw(client, operation);
        Assert.Equal(WorkerFailureCategories.AccessDenied, result.FailureCategory);
        Assert.Equal(WorkerDispatchState.NotSent, result.DispatchState);
        AssertNoTransport(client);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LifecycleSingleCall_ReadOnlyCannotBootstrapOrRebind(bool invalidated, bool dryRun)
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(@"C:\Fixture\Line.ap21");
        if (invalidated) binding.Invalidate("test invalidation");
        var before = binding.CaptureSnapshot();
        using var client = CreateClient(binding, "worker-must-not-start.exe", McpAccessMode.ReadOnly);
        var execution = LifecycleTestCalls.Execution(client, audit);
        var calls = new Func<Task<ModelContextProtocol.Protocol.CallToolResult>>[]
        {
            () => ProjectWriteTools.OpenProject(client, execution, @"C:\Fixture\Other.ap21", forceRebind: true, dryRun: dryRun),
            () => ProjectWriteTools.CreateProject(client, execution, audit.Path, "Fixture", dryRun: dryRun),
            () => ProjectWriteTools.SaveProject(client, execution, dryRun: dryRun),
            () => ProjectWriteTools.SaveProjectAs(client, execution, audit.Path, "Copy", dryRun: dryRun),
            () => ProjectWriteTools.ArchiveProject(client, execution, audit.Path, "Archive", dryRun: dryRun),
            () => ProjectWriteTools.CloseProject(client, execution, dryRun: dryRun)
        };
        foreach (var call in calls)
        {
            LifecycleTestCalls.Rejected(await call(), WorkerFailureCategories.AccessDenied);
            Assert.True(before.SameBinding(binding.CaptureSnapshot()));
            AssertNoTransport(client);
        }
        Assert.Equal(6, LifecycleTestCalls.AuditCount(audit));
    }

    [Fact]
    public async Task MissingPolicy_StillEnforcesReadWriteDefault()
    {
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null), workerExecutablePath: "worker-must-not-start.exe");
        var result = await InvokeRaw(client, "start_plc");
        Assert.Equal(WorkerFailureCategories.AccessDenied, result.FailureCategory);
        AssertNoTransport(client);
    }

    [Theory]
    [MemberData(nameof(RestrictedOperations))]
    public void WorkerAuthorization_DeniesRetiredOnlineControlInEveryWritableMode(string operation)
    {
        foreach (var mode in new[] { McpAccessMode.ReadWrite, McpAccessMode.Full })
            Assert.Equal(WorkerFailureCategories.AccessDenied,
                WorkerOperationAuthorization.Authorize(mode, operation)!.FailureCategory);
    }

    [Fact]
    public void WorkerFullMode_ParsesAndAllowsTiaConfirmations()
    {
        Assert.Equal(McpAccessMode.Full, WorkerOperationAuthorization.ParseAccessMode(new[] { "--access-mode", "full" }));
        Assert.Equal(McpAccessMode.Full, WorkerOperationAuthorization.ParseAccessMode(new[] { "--access-mode=FULL", "--access-mode", " full " }));
        Assert.Equal(McpAccessMode.ReadOnly, WorkerOperationAuthorization.ParseAccessMode(new[] { "--access-mode=full", "--access-mode=read-write" }));
        Assert.True(WorkerOperationAuthorization.AllowsTiaConfirmations(McpAccessMode.Full));
        Assert.False(WorkerOperationAuthorization.AllowsTiaConfirmations((McpAccessMode)999));
        Assert.Equal("full", WriteAuditRecord.ModeName(McpAccessMode.Full));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadWrite_InitialAttachmentStillWorks_FromStartupOrUiOpen(bool startupPath)
    {
        const string project = "ok";
        // startupPath exercises the host's --project assertion against a UI-open project.
        using var uiOpen = new FakeWorkerUiOpenProject(project);
        var openPath = ProjectPathNormalization.Canonicalize(project)!;
        var binding = new ProjectSessionBinding(startupPath ? project : null);
        using var client = CreateClient(binding, FakeWorkerLocator.Locate(), McpAccessMode.ReadWrite);
        var observed = await client.GetProjectStatusAsync(openPath);
        Assert.True(observed.Success, observed.Error);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, project);
        Assert.True(binding.IsVerified);
        var before = binding.CaptureSnapshot();
        var refused = await client.GetProjectStatusAsync(@"C:\Fixture\Other.ap21");
        Assert.Equal(WorkerFailureCategories.BindingConflict, refused.FailureCategory);
        Assert.True(before.SameBinding(binding.CaptureSnapshot()));
        var next = await client.GetProjectStatusAsync(openPath);
        Assert.True(next.Success, next.Error);
        Assert.Equal("{\"seq\":3}", next.Payload);
    }

    [Fact]
    public async Task ConfiguredProjectCannotBePromotedFromUnopenedCannedStatus()
    {
        using var uiOpen = new FakeWorkerUiOpenProject(null);
        var binding = new ProjectSessionBinding("ok");
        using var client = CreateClient(binding, FakeWorkerLocator.Locate(), McpAccessMode.ReadWrite);

        var observed = await client.GetProjectStatusAsync("ok");

        Assert.False(observed.Success);
        Assert.False(binding.IsVerified);
        Assert.Null(observed.SessionIdentity?.ProjectPath);
    }

    [Fact]
    public async Task WorkerLaunch_PropagatesFullAcrossRestart()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath("ok");
        using var audit = new TempAuditDirectory();
        var log = Path.Combine(audit.Path, "launches.jsonl");
        Directory.CreateDirectory(audit.Path);
        var previous = Environment.GetEnvironmentVariable("TIA_MCP_FAKE_WORKER_LAUNCH_LOG");
        try
        {
            Environment.SetEnvironmentVariable("TIA_MCP_FAKE_WORKER_LAUNCH_LOG", log);
            var binding = new ProjectSessionBinding(null);
            using var client = CreateClient(binding, FakeWorkerLocator.Locate(), McpAccessMode.Full);
            await FakeWorkerBinding.BindVerifiedAsync(client, binding, "ok");
            var crash = await InvokeRawCrash(client);
            Assert.Equal(WorkerFailureCategories.WorkerCrashed, crash.FailureCategory);
            Assert.Equal(ProjectBindingSnapshot.InvalidatedState, binding.BindingState);
            Assert.True(binding.Bind("ok", forceRebind: true, out var error), error);
            await FakeWorkerBinding.BindVerifiedAsync(client, binding, "ok");
            Assert.True(File.Exists(log), "FakeWorker must record its actual launch arguments.");
            var launches = File.ReadAllLines(log).Select(line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Equal(2, launches.Length);
                Assert.NotEqual(launches[0].RootElement.GetProperty("processId").GetInt32(), launches[1].RootElement.GetProperty("processId").GetInt32());
                foreach (var launch in launches)
                    Assert.Equal(new[] { "--access-mode", "full" }, launch.RootElement.GetProperty("args").EnumerateArray().Select(x => x.GetString()).ToArray());
            }
            finally { foreach (var launch in launches) launch.Dispose(); }
        }
        finally { Environment.SetEnvironmentVariable("TIA_MCP_FAKE_WORKER_LAUNCH_LOG", previous); }
    }

    private static Task<WorkerCallResult> InvokeRawCrash(OpennessWorkerClient client)
        => (Task<WorkerCallResult>)typeof(OpennessWorkerClient)
            .GetMethod("InvokeWorkerAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(client, new object[] { new WorkerRequest { Method = "browse_project_tree_v3_snapshot", ProjectDirectory = "crash" } })!;
}
