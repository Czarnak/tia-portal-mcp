using System.Reflection;
using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Vci;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public sealed class VciWritePlannerTests
{
    private static VciOperationRequest Item(string operation = "export", string id = "a", string workspace = "Fixture")
        => new() { OperationId = id, Operation = operation, WorkspaceName = workspace,
            Scope = operation is "create_workspace" or "delete_workspace" ? null : [new() { NodeType = "Device", Name = "Fixture" }] };

    [Fact]
    public async Task IndependentItemsUseOnePlanEachAndMapEffects()
    {
        using var f = await Fixture.Create();
        f.Put(new() { Leaves = 5, MappedLeaves = 2, FormatFallbacks = 1, WorkspaceRootPath = f.Root });
        var first = Item(); first.Format = "sCl"; first.RelativeDirectory = "out";
        var result = await new VciWritePlanner(f.Client).PlanAsync([first, Item(id: "b", workspace: "Other")]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal(2, f.Plans.Length);
        Assert.All(f.Plans, r => Assert.Null(r.VciQuery));
        Assert.Equal("sCl", f.Plans[0].VciWrite!.Format);
        Assert.Equal("out", f.Plans[0].VciWrite!.RelativeDirectory);
        var effect = result.Plan.Items[0].Effect!;
        Assert.Equal("export", effect.Operation); Assert.Equal("Fixture", effect.WorkspaceName);
        Assert.Equal(first.Scope, effect.Scope); Assert.Equal(f.Root, effect.RootPath);
        Assert.Equal(5, effect.Leaves); Assert.Equal(2, effect.MappedLeaves); Assert.Equal(1, effect.FormatFallbacks);
    }

    [Fact]
    public async Task PlanMembersMapToBoundedGuards()
    {
        using var f = await Fixture.Create();
        f.Put(new() { KeyCollisions = Enumerable.Range(0, 12).Select(i => "collision-" + i).ToList(),
            Unrepresentable = ["CON: reserved"], WorkspaceConflict = "duplicate", Unverifiable = "unreadable" });
        var result = await new VciWritePlanner(f.Client).PlanAsync([Item()]);
        Assert.True(result.Plan.Success);
        var guards = result.Guards["a"];
        Assert.Equal(4, guards.Count);
        Assert.All(guards, g => Assert.Equal("a", g.OperationId));
        Assert.Contains(guards, g => g.Id == "vci_workspace_exists" && g.Message.Contains("duplicate"));
        Assert.Contains(guards, g => g.Id == "vci_name_not_representable" && g.Message.Contains("CON"));
        Assert.Contains(guards, g => g.Id == "vci_state_unverifiable" && g.Message.Contains("unreadable"));
        var collision = Assert.Single(guards, g => g.Id == "vci_file_key_collision");
        Assert.Contains("collision-9", collision.Message); Assert.DoesNotContain("collision-10", collision.Message);
    }

    [Theory]
    [InlineData("delete_workspace", "vci_files_remain")]
    [InlineData("delete_mapping", "vci_files_remain")]
    [InlineData("sync_to_project", "vci_import_unvalidated")]
    [InlineData("export", null)]
    [InlineData("sync_to_workspace", null)]
    public async Task InfoGuardsFirePerOperation(string operation, string? guard)
    {
        using var f = await Fixture.Create();
        var result = await new VciWritePlanner(f.Client).PlanAsync([Item(operation)]);
        Assert.True(result.Plan.Success);
        if (guard is null) Assert.Empty(result.Guards["a"]);
        else Assert.Equal(guard, Assert.Single(result.Guards["a"]).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RootInsideProjectFolder_FailsPlan(bool dependent)
    {
        using var f = await Fixture.Create();
        var item = Item("create_workspace", dependent ? "b" : "a");
        item.RootPath = Path.Combine(Path.GetDirectoryName(f.Client.BindingSnapshot.ProjectPath)!, "workspace");
        var result = await new VciWritePlanner(f.Client).PlanAsync(dependent ? [Item("delete_workspace"), item] : [item]);
        Assert.False(result.Plan.Success); Assert.Equal("validation_error", result.Plan.Error!.Category);
        Assert.Equal(dependent ? 1 : 0, f.Plans.Length);
    }

    [Theory]
    [InlineData("target_not_found")]
    [InlineData("worker_operation_failed")]
    [InlineData("target_kind_unsupported")]
    public async Task WorkerFailurePassesThroughPlanAndReplan(string category)
    {
        using var f = await Fixture.Create(); f.Fail(category, "fixture failure");
        var planner = new VciWritePlanner(f.Client);
        var result = await planner.PlanAsync([Item()]);
        Assert.False(result.Plan.Success); Assert.Empty(result.Guards);
        Assert.Equal(category, result.Plan.Error!.Category); Assert.Equal("fixture failure", result.Plan.Error.Message);
        var late = await planner.ReplanAsync(Item());
        Assert.False(late.Replan.Success); Assert.Equal(category, late.Replan.Error!.Category);
    }

    [Fact]
    public async Task ExportAfterCreateInSameCall_DependsOnCreate()
    {
        using var f = await Fixture.Create();
        var create = Item("create_workspace"); create.RootPath = f.Root;
        var planner = new VciWritePlanner(f.Client);
        var result = await planner.PlanAsync([create, Item(id: "b", workspace: "FIXTURE"), Item("delete_mapping", "c")]);
        Assert.True(result.Plan.Success);
        Assert.Single(f.Plans);
        Assert.Equal("a", result.Plan.Items[1].DependsOn); Assert.Null(result.Plan.Items[1].Effect);
        Assert.Equal("b", result.Plan.Items[2].DependsOn); Assert.Null(result.Plan.Items[2].Effect);
        Assert.Empty(result.Guards["b"]);
        f.Put(new() { Leaves = 4, MappedLeaves = 1, WorkspaceRootPath = f.Root });
        var replan = await planner.ReplanAsync(Item(id: "b"));
        Assert.True(replan.Replan.Success); Assert.Equal(4, replan.Replan.Plan!.Effect!.Leaves);
        Assert.Null(replan.Replan.Plan.DependsOn); Assert.Equal(2, f.Plans.Length);
    }

    [Fact]
    public async Task ReplanFiresLateBlock()
    {
        using var f = await Fixture.Create();
        var first = Item("create_workspace"); first.RootPath = f.Root;
        var second = Item("create_workspace", "b"); second.RootPath = f.Root;
        var planner = new VciWritePlanner(f.Client);
        var result = await planner.PlanAsync([first, second]);
        Assert.Equal("a", result.Plan.Items[1].DependsOn); Assert.Single(f.Plans);
        f.Put(new() { WorkspaceConflict = "workspace now exists" });
        var late = await planner.ReplanAsync(second);
        Assert.True(late.Replan.Success);
        Assert.Equal("vci_workspace_exists", Assert.Single(late.Guards).Id);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task InvalidTypedPlanFailsClosed(string payload)
    {
        using var f = await Fixture.Create(); f.Raw(payload);
        Assert.Equal("protocol_error", (await new VciWritePlanner(f.Client).PlanAsync([Item()])).Plan.Error!.Category);
    }

    [Theory]
    [InlineData("negative")]
    [InlineData("mapped")]
    [InlineData("fallbacks")]
    [InlineData("null-collisions")]
    [InlineData("blank-offender")]
    public async Task InconsistentPlanEvidenceFailsClosed(string fault)
    {
        using var f = await Fixture.Create();
        var info = new VciPlanInfo();
        if (fault == "negative") info.Leaves = -1;
        if (fault == "mapped") info.MappedLeaves = 1;
        if (fault == "fallbacks") info.FormatFallbacks = 1;
        if (fault == "null-collisions") info.KeyCollisions = null!;
        if (fault == "blank-offender") info.Unrepresentable = [" "];
        f.Put(info);
        Assert.Equal("protocol_error", (await new VciWritePlanner(f.Client).PlanAsync([Item()])).Plan.Error!.Category);
    }

    // A scripted peer exercises real bound dispatch without launching Siemens code.
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "vci-planner-" + Guid.NewGuid().ToString("N"));
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "vci-workspace-" + Guid.NewGuid().ToString("N"));
        public OpennessWorkerClient Client { get; }
        public WorkerRequest[] Plans => File.ReadAllLines(Path.Combine(directory, "requests"))
            .Select(l => JsonSerializer.Deserialize<WorkerRequest>(l, WorkerJson.Request)!).Where(r => r.Method == "vci_plan").ToArray();
        private readonly WorkerResponse response;
        private Fixture()
        {
            Directory.CreateDirectory(directory);
            var identity = new WorkerSessionIdentity { WorkerSessionId = "planner", SessionGeneration = 1, PortalProcessId = 4242, ProjectPath = Path.Combine(directory, "fixture.ap21") };
            var binding = new ProjectSessionBinding(null);
            Assert.True(binding.BindVerified(identity, forceRebind: false, out _));
            Client = new(binding);
            var script = """
                param([string]$Root)
                while (($line = [Console]::In.ReadLine()) -ne $null) {
                    $r = $line | ConvertFrom-Json
                    [IO.File]::AppendAllText((Join-Path $Root 'requests'), $line + [Environment]::NewLine)
                    $file = if ($r.method -eq 'hello') { 'hello' } elseif ($r.method -eq 'vci_plan') { 'plan' } else { 'status' }
                    [Console]::Out.WriteLine([IO.File]::ReadAllText((Join-Path $Root $file))); [Console]::Out.Flush()
                }
                """;
            File.WriteAllText(Path.Combine(directory, "worker.ps1"), script);
            response = new WorkerResponse { Success = true, Payload = "{}", SessionIdentity = identity, ResolvedProjectPath = identity.ProjectPath };
            File.WriteAllText(Path.Combine(directory, "status"), JsonSerializer.Serialize(response, WorkerJson.Envelope));
            File.WriteAllText(Path.Combine(directory, "hello"), JsonSerializer.Serialize(new WorkerResponse { Success = true, ProtocolVersion = WorkerProtocol.Version, Capabilities = WorkerProtocol.RequiredCapabilities.ToList() }, WorkerJson.Envelope));
            var transport = new PersistentWorkerTransport(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), TimeSpan.FromSeconds(15), workerArgs: $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{Path.Combine(directory, "worker.ps1")}\" -Root \"{directory}\"");
            typeof(OpennessWorkerClient).GetField("_transport", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Client, transport);
            Put(new());
        }
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); Assert.True((await f.Client.GetProjectStatusAsync(null)).Success); return f;
        }
        public void Put(VciPlanInfo info) => Raw(WorkerJson.SerializePayload(info));
        public void Raw(string payload)
        {
            response.Payload = payload; Save(response);
        }
        public void Fail(string category, string error)
        {
            Save(new() { Success = false, FailureCategory = category, Error = error,
                SessionIdentity = response.SessionIdentity, ResolvedProjectPath = response.ResolvedProjectPath });
        }
        private void Save(WorkerResponse value) => File.WriteAllText(Path.Combine(directory, "plan"), JsonSerializer.Serialize(value, WorkerJson.Envelope));
        public void Dispose() { Client.Dispose(); Directory.Delete(directory, true); }
    }
}
