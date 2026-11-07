using System.Reflection;
using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Library;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.Safety.Pipeline;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryGuardedWriteDomainTests
{
    [Fact]
    public void ConfirmsCall_TrueOnlyWithLifecycleItem()
    {
        using var client = new OpennessWorkerClient(new(null));
        var domain = new LibraryWriteDomain(client);
        foreach (var operation in LibraryWriteMethods.Operations)
            Assert.Equal(LibraryWriteMethods.LifecycleOperations.Contains(operation), domain.ConfirmsCall([Item("a", operation)]));
        Assert.False(domain.ConfirmsCall([]));
        Assert.True(domain.ConfirmsCall([Item("a"), Item("b", "save_global")]));
        var description = domain.DescribeForConfirmation([Item("a"), Item("b", "save_global")], []);
        Assert.Contains("save_global", description);
        Assert.Contains("Fixture", description);
        Assert.Equal("access_denied", domain.Validate([Item("a")], McpAccessMode.ReadOnly).Error!.Category);
        Assert.False(domain.VerificationSucceeded(null));
    }

    [Fact]
    public async Task CapabilityOperations_ProjectItemOnLocalSession_RefusedBeforeAnyMutation()
    {
        using var client = new OpennessWorkerClient(new(null));
        var global = Item("a");
        var project = Item("b"); project.Scope = "project"; project.LibraryName = null;
        var domain = new LibraryWriteDomain(client);
        Assert.Equal(new[] { "library_global_delete_master_copy", "library_project_delete_master_copy" }, domain.CapabilityOperations([global, project]));
        var gate = new FakeWriteBindingGate { AccessMode = McpAccessMode.Full,
            CurrentBinding = new(ProjectBindingSnapshot.VerifiedState, "binding", 1, "C:/Fixture/Local.amc21", "worker", 1, 42, null,
                new() { ContainerKind = ProjectContainerKinds.LocalSession }) };
        var execution = new WriteExecution(gate, new RecordingAuditSink(gate), new(LibraryGuardDefinitions.Definitions), TimeProvider.System);
        var reply = await execution.RunAsync(domain, new WriteCall<LibraryWriteItem>([global, project], false));
        Assert.Equal("unsupported_capability", reply.StructuredContent!.Value.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(0, gate.LeaseCalls);
        Assert.Null(typeof(OpennessWorkerClient).GetField("_transport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client));
    }

    [Theory]
    [InlineData("create_global", "C:/Fixture/Output", "NewLibrary")]
    [InlineData("open_global", "C:/Fixture/Input.al21", "readWrite")]
    [InlineData("retrieve_global", "C:/Fixture/Input.al21", "C:/Fixture/Output")]
    [InlineData("save_global", "Fixture", "save_global")]
    [InlineData("close_global", "Fixture", "discard")]
    [InlineData("archive_global", "Fixture", "C:/Fixture/Output")]
    public void ConfirmationDescription_NamesOperationAndRequestedTargets(string operation, string first, string second)
    {
        using var client = new OpennessWorkerClient(new(null));
        var item = Item("a", operation);
        item.Directory = "C:/Fixture/Output"; item.FilePath = "C:/Fixture/Input.al21";
        item.Name = "NewLibrary"; item.OpenMode = "readWrite"; item.CloseAction = "discard";
        var message = new LibraryWriteDomain(client).DescribeForConfirmation([item], []);
        Assert.Contains(operation, message);
        Assert.Contains(first, message);
        Assert.Contains(second, message);
        if (operation == "archive_global") Assert.Contains("NewLibrary", message);
    }

    [Fact]
    public async Task DryRun_PlansWithoutMutation()
    {
        using var fixture = await Fixture.Create();
        var reply = await fixture.Run([Item("a"), Item("b")], dryRun: true);
        var root = reply.StructuredContent!.Value;
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("preview", root.GetProperty("phase").GetString());
        Assert.Equal(2, root.GetProperty("effects").GetArrayLength());
        Assert.Equal(new[] { "library_global_plan", "library_global_plan" }, fixture.LibraryRequests.Select(r => r.Method));
    }

    [Fact]
    public async Task StopsAtFirstFailure_NoLaterMutation()
    {
        using var fixture = await Fixture.Create();
        var reply = await fixture.Run([Item("a"), Item("b"), Item("c")]);
        var root = reply.StructuredContent!.Value;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.False(reply.IsError);
        Assert.Equal(new[] { "succeeded", "failed", "skipped" }, root.GetProperty("batch").GetProperty("operations").EnumerateArray().Select(o => o.GetProperty("status").GetString()));
        Assert.Equal(new[] { "a", "b" }, fixture.LibraryRequests.Where(r => r.Method != LibraryWriteMethods.GlobalPlan).Select(r => r.LibraryWrite!.OperationId));
        Assert.Equal("superseded", Assert.Single(root.GetProperty("verification").GetProperty("operations").EnumerateArray()).GetProperty("check").GetString());
    }

    [Fact]
    public async Task SuccessfulReplanning_ReplacesVerificationEffects()
    {
        using var fixture = await Fixture.Create();
        var items = new[] { Item("a"), Item("b") };
        var domain = new LibraryWriteDomain(fixture.Client);
        Assert.True((await domain.PlanAsync(items)).Success);
        fixture.PutPlan("C:/Fixture/First.al21", "C:/Fixture/Second.al21");
        Assert.True((await domain.PlanAsync(items)).Success);
        await domain.MutateAsync(items[0]);
        var success = domain.Project(items[0], WorkerCallResult.Ok(WorkerJson.SerializePayload(new LibraryWriteResultInfo())));
        var batch = StructuredOperationBatch.FromItems([success, StructuredOperationItem.Failed(items[1], "worker_operation_failed", "not attempted", [])]);
        await domain.MutateAsync(items[1]);
        Assert.Equal("trusted", Assert.Single((await domain.VerifyAsync(batch))!.Operations).Check);
        await Assert.ThrowsAsync<InvalidOperationException>(() => domain.ReplanAsync(items[0]));
    }

    [Fact]
    public async Task Verification_IgnoresUnattemptedFailuresOnTheSameObject()
    {
        using var fixture = await Fixture.Create();
        var items = new[] { Item("a"), Item("b") };
        var domain = new LibraryWriteDomain(fixture.Client);
        Assert.True((await domain.PlanAsync(items)).Success);
        await domain.MutateAsync(items[0]);
        var success = domain.Project(items[0], WorkerCallResult.Ok(WorkerJson.SerializePayload(new LibraryWriteResultInfo())));
        var batch = StructuredOperationBatch.FromItems([success,
            StructuredOperationItem.Failed(items[1], "worker_operation_failed", "not attempted", [])]);
        Assert.Equal("trusted", Assert.Single((await domain.VerifyAsync(batch))!.Operations).Check);
    }

    private static LibraryWriteItem Item(string id, string operation = "delete_master_copy")
        => new() { OperationId = id, Operation = operation, Scope = "global", LibraryName = "Fixture", Name = "Copy" };

    // Script only the external peer: planning, dispatch, stop semantics and verification stay real.
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "library-domain-" + Guid.NewGuid().ToString("N"));
        private readonly WorkerSessionIdentity identity;
        public OpennessWorkerClient Client { get; }
        public WorkerRequest[] LibraryRequests => File.ReadAllLines(Path.Combine(directory, "requests"))
            .Select(l => JsonSerializer.Deserialize<WorkerRequest>(l, WorkerJson.Request)!).Where(r => r.Method.StartsWith("library_", StringComparison.Ordinal)).ToArray();
        private Fixture()
        {
            Directory.CreateDirectory(directory);
            identity = new() { WorkerSessionId = "domain", SessionGeneration = 1, PortalProcessId = 4242, ProjectPath = Path.Combine(directory, "fixture.ap21") };
            var binding = new ProjectSessionBinding(null);
            Assert.True(binding.BindVerified(identity, false, out _));
            Client = new(binding);
            File.WriteAllText(Path.Combine(directory, "worker.ps1"), """
                param([string]$Root)
                while (($line = [Console]::In.ReadLine()) -ne $null) {
                    $r = $line | ConvertFrom-Json
                    [IO.File]::AppendAllText((Join-Path $Root 'requests'), $line + [Environment]::NewLine)
                    $file = if ($r.method -eq 'hello') { 'hello' } elseif ($r.method -eq 'library_global_plan') { if ($r.libraryWrite.operationId -eq 'b') { 'plan-b' } else { 'plan' } } elseif ($r.method -eq 'library_global_delete_master_copy') { if ($r.libraryWrite.operationId -eq 'b') { 'failure' } else { 'mutation' } } else { 'status' }
                    [Console]::Out.WriteLine([IO.File]::ReadAllText((Join-Path $Root $file))); [Console]::Out.Flush()
                }
                """);
            Save("hello", new() { Success = true, ProtocolVersion = WorkerProtocol.Version, Capabilities = WorkerProtocol.RequiredCapabilities.ToList() });
            Save("status", Response("{}"));
            Save("mutation", Response(WorkerJson.SerializePayload(new LibraryWriteResultInfo())));
            Save("failure", new() { Success = false, FailureCategory = "worker_operation_failed", Error = "fixture rejection", SessionIdentity = identity, ResolvedProjectPath = identity.ProjectPath });
            PutPlan("C:/Fixture/First.al21");
            var transport = new PersistentWorkerTransport(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                TimeSpan.FromSeconds(15), workerArgs: $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{Path.Combine(directory, "worker.ps1")}\" -Root \"{directory}\"");
            typeof(OpennessWorkerClient).GetField("_transport", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Client, transport);
        }
        public static async Task<Fixture> Create()
        {
            var fixture = new Fixture(); Assert.True((await fixture.Client.GetProjectStatusAsync(null)).Success); return fixture;
        }
        public void PutPlan(string path, string? secondPath = null)
        {
            Save("plan", Plan(path));
            Save("plan-b", Plan(secondPath ?? path));
        }
        private WorkerResponse Plan(string path) => Response(WorkerJson.SerializePayload(new LibraryWritePlanInfo
        {
            Library = new() { Scope = "global", Kind = "library", LibraryPath = path, Name = "Fixture" },
            Target = new() { Scope = "global", Kind = "masterCopy", FolderKind = "masterCopies", Name = "Copy" }
        }));
        public Task<ModelContextProtocol.Protocol.CallToolResult> Run(LibraryWriteItem[] items, bool dryRun = false)
        {
            var gate = new FakeWriteBindingGate { AccessMode = McpAccessMode.Full, CurrentBinding = Client.BindingSnapshot };
            return new WriteExecution(gate, new RecordingAuditSink(gate), new(LibraryGuardDefinitions.Definitions), TimeProvider.System)
                .RunAsync(new LibraryWriteDomain(Client), new WriteCall<LibraryWriteItem>(items, dryRun));
        }
        private WorkerResponse Response(string payload) => new() { Success = true, Payload = payload, SessionIdentity = identity, ResolvedProjectPath = identity.ProjectPath };
        private void Save(string file, WorkerResponse value) => File.WriteAllText(Path.Combine(directory, file), JsonSerializer.Serialize(value, WorkerJson.Envelope));
        public void Dispose() { Client.Dispose(); Directory.Delete(directory, true); }
    }
}
