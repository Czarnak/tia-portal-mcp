using System.Reflection;
using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
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
        "open_project", "create_project", "save_project", "save_project_as", "archive_project",
        "close_project", "probe_project_status_for_lifecycle", "probe_open_project_rebind", "start_plc", "stop_plc"
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
    public async Task ReadWrite_DeniesLifecycleAndOnlineControlBeforeTransport(string operation)
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding, "worker-must-not-start.exe", McpAccessMode.ReadWrite);
        var result = await InvokeRaw(client, operation);
        Assert.Equal(WorkerFailureCategories.AccessDenied, result.FailureCategory);
        Assert.Equal(WorkerDispatchState.NotSent, result.DispatchState);
        AssertNoTransport(client);
    }

    [Theory]
    [InlineData("start_plc")]
    [InlineData("stop_plc")]
    public async Task MixedBatch_ReadWriteRejectsBeforeBindingSnapshotsOrMutations(string operation)
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(@"C:\Fixture\Line.ap21");
        var before = binding.CaptureSnapshot();
        using var client = CreateClient(binding, "worker-must-not-start.exe", McpAccessMode.ReadWrite);
        var safety = new WriteSafetyService(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "edit", Operation = "update_tag", TableName = "Tags", Name = "Tag" },
            new BatchOperationRequest { OperationId = "control", Operation = operation }
        };
        foreach (var result in new[]
        {
            await WriteBatchTools.PreviewWriteBatch(client, safety, operations),
            await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: "unknown")
        })
        {
            using var json = JsonDocument.Parse(result);
            Assert.Equal(WorkerFailureCategories.AccessDenied, json.RootElement.GetProperty("failureCategory").GetString());
            Assert.Contains("read-write", json.RootElement.GetProperty("error").GetString());
            Assert.False(json.RootElement.TryGetProperty("safetyToken", out _));
        }
        Assert.True(before.SameBinding(binding.CaptureSnapshot()));
        AssertNoTransport(client);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LifecyclePreview_ReadWriteCannotBootstrapOrRebind(bool invalidated)
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(@"C:\Fixture\Line.ap21");
        if (invalidated) binding.Invalidate("test invalidation");
        var before = binding.CaptureSnapshot();
        using var client = CreateClient(binding, "worker-must-not-start.exe", McpAccessMode.ReadWrite);
        var safety = new WriteSafetyService(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
        var calls = new Func<Task<string>>[]
        {
            () => ProjectWriteTools.OpenProject(client, safety, @"C:\Fixture\Other.ap21", forceRebind: true),
            () => ProjectWriteTools.CreateProject(client, safety, audit.Path, "Fixture"),
            () => ProjectWriteTools.SaveProject(client, safety),
            () => ProjectWriteTools.SaveProjectAs(client, safety, audit.Path, "Copy"),
            () => ProjectWriteTools.ArchiveProject(client, safety, audit.Path, "Archive"),
            () => ProjectWriteTools.CloseProject(client, safety)
        };
        foreach (var call in calls)
        {
            using var json = JsonDocument.Parse(await call());
            Assert.Equal(WorkerFailureCategories.AccessDenied, json.RootElement.GetProperty("failureCategory").GetString());
            Assert.False(json.RootElement.TryGetProperty("safetyToken", out _));
            Assert.True(before.SameBinding(binding.CaptureSnapshot()));
            AssertNoTransport(client);
        }
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
    public void WorkerAuthorization_ReadWriteCeilingMatchesHost(string operation)
    {
        Assert.Equal(WorkerFailureCategories.AccessDenied,
            WorkerOperationAuthorization.Authorize(McpAccessMode.ReadWrite, operation)!.FailureCategory);
        Assert.Null(WorkerOperationAuthorization.Authorize(McpAccessMode.Full, operation));
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
    public async Task ReadWrite_InitialAttachmentStillWorks_AndReadCannotSwitchAttachedProject(bool startupPath)
    {
        const string project = "ok";
        var binding = new ProjectSessionBinding(startupPath ? project : null);
        using var client = CreateClient(binding, FakeWorkerLocator.Locate(), McpAccessMode.ReadWrite);
        var observed = await client.GetProjectStatusAsync(project);
        Assert.True(observed.Success, observed.Error);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, project);
        Assert.True(binding.IsVerified);
        var before = binding.CaptureSnapshot();
        var refused = await client.GetProjectStatusAsync(@"C:\Fixture\Other.ap21");
        Assert.Equal(WorkerFailureCategories.BindingConflict, refused.FailureCategory);
        Assert.True(before.SameBinding(binding.CaptureSnapshot()));
    }

    [Fact]
    public async Task WorkerLaunch_PropagatesFullAcrossRestart()
    {
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
