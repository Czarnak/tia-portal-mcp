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
    [Theory]
    [InlineData("activation", true)]
    [InlineData("editing", true)]
    [InlineData("reference", true)]
    [InlineData("different-library", false)]
    [InlineData("different-culture", false)]
    [InlineData("second-missing", false)]
    [InlineData("all-missing", true)]
    [InlineData("no-producer", false)]
    public async Task MetadataCommentProducer_MatchesLibraryAndCulture(string scenario, bool supported)
    {
        using var f = await Fixture.Create();
        var root = new LibraryIdentityInfo { Scope = "global", Kind = "library", LibraryPath = @"C:\Libs\Fixture.al21" };
        var other = new LibraryIdentityInfo { Scope = "global", Kind = "library", LibraryPath = @"C:\Other\Fixture.al21" };
        var missing = new LibraryIdentityInfo { Scope = "global", Kind = "comment", LibraryPath = root.LibraryPath, Name = "pl-PL" };
        f.PutFor("a", new() { Library = root });
        f.PutFor("b", new() { Library = scenario == "different-library" ? other : root });
        f.PutFor("c", new() { Library = root, MissingTargets = scenario is "second-missing" or "all-missing" ? [missing,
            new() { Scope = "global", Kind = "comment", LibraryPath = root.LibraryPath, Name = "de-DE" }] : [missing] });
        var producer = new LibraryWriteItem { OperationId = "b", Operation = "set_languages", Scope = "global", LibraryPath = root.LibraryPath };
        if (scenario == "editing") producer.EditingLanguage = "PL-pl";
        else if (scenario == "reference") producer.ReferenceLanguage = "PL-pl";
        else producer.ActivateLanguages = [scenario == "all-missing" ? "de-DE" : scenario is "different-culture" or "no-producer" ? "fr-FR" : "PL-pl"];
        var result = await new LibraryWritePlanner(f.Client).PlanAsync([
            new() { OperationId = "a", Operation = "set_languages", Scope = "global", LibraryPath = root.LibraryPath,
                ActivateLanguages = [scenario == "no-producer" ? "en-US" : "pl-PL"] },
            producer,
            new() { OperationId = "c", Operation = "set_comments", Scope = "global", LibraryPath = root.LibraryPath, Comments = [new() { Culture = "pl-PL", Text = "new" }] }]);
        // The first producer remains eligible unless the consumer has another missing culture.
        Assert.Equal(supported || scenario is "different-library" or "different-culture", result.Plan.Success);
        if (result.Plan.Success) Assert.Equal(supported ? 2 : 1, result.Plan.Items[2].Effect!.CreatedByItem);
        else Assert.Equal("target_not_found", result.Plan.Error!.Category);
    }

    [Fact]
    public async Task MetadataPreview_ProjectsRequestedChangeIntoEffect()
    {
        using var f = await Fixture.Create();
        f.Put(new() { Library = new() { Scope = "global", Kind = "library", LibraryPath = @"C:\Libs\Fixture.al21" },
            MetadataChange = new() { Comments = [new() { Culture = "pl-PL", Before = null, After = "new" }] } });
        var result = await new LibraryWritePlanner(f.Client).PlanAsync([
            new() { OperationId = "a", Operation = "set_comments", Scope = "global", LibraryName = "Fixture",
                Comments = [new() { Culture = "pl-PL", Text = "new" }] }]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        var comment = Assert.Single(result.Plan.Items[0].Effect!.MetadataChange!.Comments!);
        Assert.Equal("pl-PL", comment.Culture); Assert.Null(comment.Before); Assert.Equal("new", comment.After);
        Assert.Empty(result.Guards["a"]);
    }

    [Theory]
    [InlineData("comment")]
    [InlineData("culture")]
    [InlineData("activatedLanguage")]
    public async Task MetadataPreview_NullChangeEntriesRejectAllPlanEvidence(string member)
    {
        using var f = await Fixture.Create();
        var change = new LibraryMetadataChangeInfo();
        if (member == "comment") change.Comments = [null!];
        if (member == "culture") change.Comments = [new() { Culture = null!, After = "leak-sentinel" }];
        if (member == "activatedLanguage") change.ActivatedLanguages = [null!];
        f.Put(new() { MetadataChange = change });
        var result = await new LibraryWritePlanner(f.Client).PlanAsync([Item("a")]);
        Assert.Equal("protocol_error", result.Plan.Error!.Category);
        Assert.Empty(result.Plan.Items); Assert.Empty(result.Guards);
        Assert.DoesNotContain("leak-sentinel", TiaMcpServer.Json.CanonicalJson.Serialize(result));
    }

    [Fact]
    public async Task Planner_TargetCreatedByEarlierItem_CreatedByItem()
    {
        using var f = await Fixture.Create();
        var root = new LibraryIdentityInfo { Scope = "project", Kind = "library" };
        var target = new LibraryIdentityInfo { Scope = "project", Kind = "type", FolderKind = "types", Name = "Motor" };
        f.PutFor("a", new() { Library = root, Target = target });
        f.PutFor("b", new() { Library = root, MissingTargets = [target] });
        var result = await new LibraryWritePlanner(f.Client).PlanAsync([
            new() { OperationId = "a", Operation = "create_type", PlcName = "PLC", PlcKind = "block", PlcObjectName = "Motor" },
            new() { OperationId = "b", Operation = "delete_type", Scope = "project", TypeName = "Motor" }]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal(1, result.Plan.Items[1].Effect!.CreatedByItem);
        Assert.Null(result.Plan.Items[1].DependsOn);
    }

    [Fact]
    public async Task Planner_CreateSynchronizeInstantiateChain_EachNamesItsProducer()
    {
        using var f = await Fixture.Create();
        var project = new LibraryIdentityInfo { Scope = "project", Kind = "library" };
        var global = new LibraryIdentityInfo { Scope = "global", Kind = "library", LibraryPath = @"C:\Libs\Fixture.al21" };
        var created = new LibraryIdentityInfo { Scope = "project", Kind = "type", FolderKind = "types", Name = "Motor" };
        f.PutFor("a", new() { Library = project, Target = created });
        // A synchronize whose source comes from an earlier item describes its type by selector (worker plan reader).
        f.PutFor("b", new() { Library = global, MissingTargets = [created], Type = new() { TypeName = "Motor" } });
        f.PutFor("c", new() { Library = project, MissingTargets = [new() { Scope = "global", Kind = "type", FolderKind = "types",
            LibraryPath = global.LibraryPath, Name = "Motor" }] });
        var result = await new LibraryWritePlanner(f.Client).PlanAsync([
            new() { OperationId = "a", Operation = "create_type", PlcName = "PLC", PlcKind = "block", PlcObjectName = "Motor" },
            new() { OperationId = "b", Operation = "synchronize_type", SourceScope = "project", TypeName = "Motor", Scope = "global", LibraryPath = global.LibraryPath },
            new() { OperationId = "c", Operation = "instantiate_type_version", SourceScope = "global", SourceLibraryPath = global.LibraryPath,
                TypeName = "Motor", PlcName = "PLC" }]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal(1, result.Plan.Items[1].Effect!.CreatedByItem);
        Assert.Equal(2, result.Plan.Items[2].Effect!.CreatedByItem);
    }

    [Fact]
    public async Task Planner_UnrelatedMissingTarget_FailsCall()
    {
        using var f = await Fixture.Create();
        var root = new LibraryIdentityInfo { Scope = "project", Kind = "library" };
        f.PutFor("a", new() { Library = root, Target = new() { Scope = "project", Kind = "folder", FolderKind = "types", FolderPath = ["New"], Name = "New" } });
        f.PutFor("b", new() { Library = root, MissingTargets = [new() { Scope = "project", Kind = "type", FolderKind = "types", FolderPath = ["New"], Name = "Missing" }] });
        var result = await new LibraryWritePlanner(f.Client).PlanAsync([
            new() { OperationId = "a", Operation = "create_folder", Scope = "project", FolderKind = "types", Name = "New" },
            new() { OperationId = "b", Operation = "delete_type", Scope = "project", TypeName = "Missing", FolderPath = ["New"] }]);
        Assert.Equal("target_not_found", result.Plan.Error!.Category);
    }
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

    [Theory]
    [InlineData("library")]
    [InlineData("target")]
    [InlineData("folderContents")]
    public async Task NullFolderSegmentInAnyIdentityRejectsAllPlanEvidence(string position)
    {
        using var f = await Fixture.Create();
        var identity = new LibraryIdentityInfo { Name = "leak-sentinel", FolderPath = [null!] };
        var plan = new LibraryWritePlanInfo();
        if (position == "library") plan.Library = identity;
        if (position == "target") plan.Target = identity;
        if (position == "folderContents") plan.FolderContents = [identity];
        f.Put(plan);
        var result = await new LibraryWritePlanner(f.Client).PlanAsync([Item("a")]);
        Assert.False(result.Plan.Success);
        Assert.Equal("protocol_error", result.Plan.Error!.Category);
        Assert.Empty(result.Plan.Items);
        Assert.Empty(result.Guards);
        Assert.DoesNotContain("leak-sentinel", TiaMcpServer.Json.CanonicalJson.Serialize(result));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullOptionalIdentitiesAndEmptyFolderPathsRemainValid(bool present)
    {
        using var f = await Fixture.Create();
        f.Put(new() { Library = present ? new() : null, Target = present ? new() : null,
            FolderContents = present ? [new()] : [] });
        var result = await new LibraryWritePlanner(f.Client).PlanAsync([Item("a")]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        var effect = Assert.Single(result.Plan.Items).Effect!;
        if (present)
        {
            Assert.Empty(effect.Library!.FolderPath);
            Assert.Empty(effect.Target!.FolderPath);
            Assert.Empty(Assert.Single(effect.FolderContents).FolderPath);
        }
        else
        {
            Assert.Null(effect.Library);
            Assert.Null(effect.Target);
        }
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
                    if ($file -eq 'plan' -and (Test-Path (Join-Path $Root ('plan-' + $r.libraryWrite.operationId)))) { $file = 'plan-' + $r.libraryWrite.operationId }
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
        public void PutFor(string id, LibraryWritePlanInfo info) => File.WriteAllText(Path.Combine(directory, "plan-" + id),
            JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = WorkerJson.SerializePayload(info),
                SessionIdentity = response.SessionIdentity, ResolvedProjectPath = response.ResolvedProjectPath }, WorkerJson.Envelope));
        public void Raw(string payload) { response.Payload = payload; Save(response); }
        public void Fail(string category, string error) => Save(new() { Success = false, FailureCategory = category, Error = error,
            SessionIdentity = response.SessionIdentity, ResolvedProjectPath = response.ResolvedProjectPath });
        private void Save(WorkerResponse value) => File.WriteAllText(Path.Combine(directory, "plan"), JsonSerializer.Serialize(value, WorkerJson.Envelope));
        public void Dispose() { Client.Dispose(); Directory.Delete(directory, true); }
    }
}
