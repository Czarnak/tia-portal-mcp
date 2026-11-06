using System.Reflection;
using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Library;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryWritePlannerTests
{
    [Fact]
    public async Task PlansEveryItemThroughBoundRouteWithNoDependencies()
    {
        using var f = await Fixture.Create();
        f.Put(new() { Library = new() { Scope = "project", Kind = "library" },
            Target = new() { Scope = "project", Kind = "masterCopy", Name = "Source" }, AlreadyOpen = true, ContentsTruncated = true });
        var items = new[] { Item("a"), Item("b") };
        items[1].Scope = "project"; items[1].LibraryName = null;
        var result = await new LibraryWritePlanner(f.Client).PlanAsync(items);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal(["library_global_plan", "library_project_plan"], f.Plans.Select(p => p.Method));
        Assert.All(f.Plans, request => { Assert.Null(request.LibraryQuery); Assert.NotNull(request.ExpectedSessionIdentity); });
        Assert.Equal("b", f.Plans[1].LibraryWrite!.OperationId);
        Assert.All(result.Plan.Items, p => { Assert.Null(p.DependsOn); Assert.Empty(p.Preconditions); });
        var effect = result.Plan.Items[0].Effect!;
        Assert.Equal("Source", effect.Target!.Name);
        Assert.Equal("project", effect.Library!.Scope);
        Assert.True(effect.AlreadyOpen);
        Assert.True(effect.ContentsTruncated);
        Assert.Empty(result.Guards["a"]);
    }

    [Fact]
    public async Task FlagsFireAllGuardsAndDiscardAlwaysInforms()
    {
        using var f = await Fixture.Create();
        f.Put(new() { MasterCopyExists = true, FolderContents = [new() { Name = "Child" }], Unverifiable = "unreadable" });
        var item = Item("a", "close_global"); item.CloseAction = "discard";
        var result = await new LibraryWritePlanner(f.Client).PlanAsync([item]);
        Assert.True(result.Plan.Success);
        Assert.Equal(new[] { "library_deletes_folder_contents", "library_discards_changes", "library_master_copy_exists", "library_state_unverifiable" },
            result.Guards["a"].Select(g => g.Id).Order());
        Assert.All(result.Guards["a"], g => Assert.Equal("a", g.OperationId));
        f.Put(new());
        result = await new LibraryWritePlanner(f.Client).PlanAsync([item]);
        Assert.Equal("library_discards_changes", Assert.Single(result.Guards["a"]).Id);
        item.CloseAction = "save";
        Assert.Empty((await new LibraryWritePlanner(f.Client).PlanAsync([item])).Guards["a"]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("{\"leak-sentinel\":1}")]
    public async Task MalformedPlanFailsClosed(string payload)
    {
        using var f = await Fixture.Create(); f.Raw(payload);
        var result = await new LibraryWritePlanner(f.Client).PlanAsync([Item("a")]);
        Assert.Equal("protocol_error", result.Plan.Error!.Category);
        Assert.DoesNotContain("leak-sentinel", result.Plan.Error.Message);
        Assert.Empty(result.Guards);
    }

    [Fact]
    public async Task NullContentEntryIsMalformed()
    {
        using var f = await Fixture.Create(); f.Put(new() { FolderContents = [null!] });
        Assert.Equal("protocol_error", (await new LibraryWritePlanner(f.Client).PlanAsync([Item("a")])).Plan.Error!.Category);
    }

    [Fact]
    public async Task TargetFailureStopsPlanningAndPassesThrough()
    {
        using var f = await Fixture.Create(); f.Fail("target_not_found", "missing");
        var result = await new LibraryWritePlanner(f.Client).PlanAsync([Item("a"), Item("b")]);
        Assert.Equal("target_not_found", result.Plan.Error!.Category);
        Assert.Equal("missing", result.Plan.Error.Message);
        Assert.Empty(result.Plan.Items);
        Assert.Empty(result.Guards);
        Assert.Single(f.Plans);
    }

    [Fact]
    public async Task UnboundWriteClientFailsBeforeDispatch()
    {
        using var client = new OpennessWorkerClient(new(null));
        Assert.Equal("binding_conflict", (await client.WriteLibraryAsync("library_global_plan", new())).FailureCategory);
    }

    private static LibraryWriteItem Item(string id, string operation = "delete_master_copy")
        => new() { OperationId = id, Operation = operation, Scope = "global", LibraryName = "Fixture", Name = "Copy" };

    // Script only the external peer; host binding, routing and typed decoding remain real.
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "library-planner-" + Guid.NewGuid().ToString("N"));
        public OpennessWorkerClient Client { get; }
        public WorkerRequest[] Plans => File.ReadAllLines(Path.Combine(directory, "requests"))
            .Select(l => JsonSerializer.Deserialize<WorkerRequest>(l, WorkerJson.Request)!)
            .Where(r => r.Method is "library_global_plan" or "library_project_plan").ToArray();
        private readonly WorkerResponse response;
        private Fixture()
        {
            Directory.CreateDirectory(directory);
            var identity = new WorkerSessionIdentity { WorkerSessionId = "planner", SessionGeneration = 1,
                PortalProcessId = 4242, ProjectPath = Path.Combine(directory, "fixture.ap21") };
            var binding = new ProjectSessionBinding(null);
            Assert.True(binding.BindVerified(identity, forceRebind: false, out _));
            Client = new(binding);
            var script = """
                param([string]$Root)
                while (($line = [Console]::In.ReadLine()) -ne $null) {
                    $r = $line | ConvertFrom-Json
                    [IO.File]::AppendAllText((Join-Path $Root 'requests'), $line + [Environment]::NewLine)
                    $file = if ($r.method -eq 'hello') { 'hello' } elseif ($r.method -in @('library_global_plan','library_project_plan')) { 'plan' } else { 'status' }
                    [Console]::Out.WriteLine([IO.File]::ReadAllText((Join-Path $Root $file))); [Console]::Out.Flush()
                }
                """;
            File.WriteAllText(Path.Combine(directory, "worker.ps1"), script);
            response = new WorkerResponse { Success = true, Payload = "{}", SessionIdentity = identity, ResolvedProjectPath = identity.ProjectPath };
            File.WriteAllText(Path.Combine(directory, "status"), JsonSerializer.Serialize(response, WorkerJson.Envelope));
            File.WriteAllText(Path.Combine(directory, "hello"), JsonSerializer.Serialize(new WorkerResponse { Success = true,
                ProtocolVersion = WorkerProtocol.Version, Capabilities = WorkerProtocol.RequiredCapabilities.ToList() }, WorkerJson.Envelope));
            var transport = new PersistentWorkerTransport(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                TimeSpan.FromSeconds(15), workerArgs: $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{Path.Combine(directory, "worker.ps1")}\" -Root \"{directory}\"");
            typeof(OpennessWorkerClient).GetField("_transport", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Client, transport);
            Put(new());
        }
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); Assert.True((await f.Client.GetProjectStatusAsync(null)).Success); return f;
        }
        public void Put(LibraryWritePlanInfo info) => Raw(WorkerJson.SerializePayload(info));
        public void Raw(string payload) { response.Payload = payload; Save(response); }
        public void Fail(string category, string error) => Save(new() { Success = false, FailureCategory = category, Error = error,
            SessionIdentity = response.SessionIdentity, ResolvedProjectPath = response.ResolvedProjectPath });
        private void Save(WorkerResponse value) => File.WriteAllText(Path.Combine(directory, "plan"), JsonSerializer.Serialize(value, WorkerJson.Envelope));
        public void Dispose() { Client.Dispose(); Directory.Delete(directory, true); }
    }
}
