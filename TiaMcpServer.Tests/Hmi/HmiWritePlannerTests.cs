using System.Reflection;
using System.Text.Json;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Hmi;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

public class HmiWritePlannerTests
{
    private static HmiWriteOperationRequest Item(string operation = "update_tag", string id = "a", string tag = "T")
        => new() { OperationId = id, Operation = operation, HmiName = "H", TagName = tag, AcquisitionCycle = "T1s" };

    [Fact]
    public async Task EvidenceIsLoadedOnceForAliasesAndPagesToTheEnd()
    {
        using var f = await Fixture.Create();
        f.Put("hmi_list_tags", new HmiTagListInfo { Tags = [Tag("T")], Page = new(0, 2000, 2, 1) { SnapshotId = "tags" } });
        f.Put("hmi_list_tags|1", new HmiTagListInfo { Tags = [Tag("U")], Page = new(1, 2000, 2, null) });
        var second = Item(id: "b", tag: "U"); second.HmiName = "Device";
        var planner = new HmiWritePlanner(f.Client);
        var result = await planner.PlanAsync([Item(), second]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal("H", planner.ResolvedHmiName("b"));
        Assert.Equal(1, f.Requests.Count(r => r.Method == "hmi_list_tag_tables"));
        var pages = f.Requests.Where(r => r.Method == "hmi_list_tags").ToArray();
        Assert.Equal(2, pages.Length); Assert.All(pages, p => Assert.Equal(2000, p.HmiQuery!.Limit));
        Assert.Equal("tags", pages[1].HmiQuery!.SnapshotId);
    }

    [Theory]
    [InlineData(0, "target_not_found")]
    [InlineData(2, "target_ambiguous")]
    public async Task OmittedHmiName_ZeroOrTwoUnified_FailsAtPlan(int count, string category)
    {
        using var f = await Fixture.Create();
        f.Put("hmi_list_hmi_devices", new HmiDeviceListInfo { Devices = Enumerable.Range(0, count).Select(i => new HmiDeviceInfo { SoftwareName = "H" + i, DeviceName = "D" + i, Kind = "unified" }).ToList() });
        var item = Item(); item.HmiName = null;
        Assert.Equal(category, (await new HmiWritePlanner(f.Client).PlanAsync([item])).Plan.Error!.Category);
    }

    [Fact]
    public async Task ClassicIsUnsupported()
    {
        using var f = await Fixture.Create();
        f.Put("hmi_list_hmi_devices", new HmiDeviceListInfo { Devices = [new() { SoftwareName = "H", Kind = "classic" }] });
        Assert.Equal("target_kind_unsupported", (await new HmiWritePlanner(f.Client).PlanAsync([Item()])).Plan.Error!.Category);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("missing")]
    [InlineData("cursor")]
    public async Task InvalidEvidenceFailsClosed(string fault)
    {
        using var f = await Fixture.Create();
        if (fault == "missing") f.PutRaw("hmi_list_tags", "{}");
        else f.Put("hmi_list_tags", new HmiTagListInfo { IsComplete = fault != "incomplete", Tags = [Tag("T")], Page = fault == "cursor" ? new(0, 2000, 2, 1) : new(0, 2000, 1, null) });
        var result = await new HmiWritePlanner(f.Client).PlanAsync([Item()]);
        if (fault == "missing") Assert.Equal("protocol_error", result.Plan.Error!.Category);
        else Assert.Contains(result.Guards["a"], g => g.Id == "hmi_state_unverifiable");
    }

    [Theory]
    [InlineData("T.Member", "discrete")]
    [InlineData("T.Member.Nested", "analog")]
    public async Task LocalMemberNamesResolveFromObservedContainment(string trigger, string kind)
    {
        using var f = await Fixture.Create();
        f.Put("hmi_get_tag|T", new HmiTagDetailInfo { Tag = Tag("T"), Members = [new() { Tag = Tag("Member"), Members = [new() { Tag = Tag("Nested") }] }] });
        var alarm = Alarm("a", trigger); alarm.AlarmKind = kind;
        var planner = new HmiWritePlanner(f.Client);
        var result = await planner.PlanAsync([alarm]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Contains(result.Plan.Items[0].Effect!.Changes, c => c.Field == "triggerTag" && c.Requested == trigger);
        var fresh = await planner.ReplanAsync(alarm);
        Assert.True(fresh.Replan.Success, fresh.Replan.Error?.Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task IncompleteOrTruncatedMembersStillBlockInitialAndFreshPlans(bool complete, bool truncated)
    {
        using var f = await Fixture.Create();
        f.Put("hmi_get_tag|T", new HmiTagDetailInfo { Tag = Tag("T"), IsComplete = complete, MembersTruncated = truncated, Members = [new() { Tag = Tag("Member") }] });
        var alarm = Alarm("a", "T.Member");
        var planner = new HmiWritePlanner(f.Client);
        var result = await planner.PlanAsync([alarm]);
        Assert.Contains(result.Guards["a"], g => g.Id == "hmi_state_unverifiable");
        var fresh = await planner.ReplanAsync(alarm);
        Assert.Contains(fresh.Guards, g => g.Id == "hmi_state_unverifiable");
    }

    [Fact]
    public async Task RenamedExistingTagQueriesOriginalThenCurrentAtReplan()
    {
        using var f = await Fixture.Create();
        var rename = Item("update_tag", "rename"); rename.NewName = "U";
        var delete = Item("delete_tag", "delete", "U");
        var planner = new HmiWritePlanner(f.Client);
        var plan = await planner.PlanAsync([rename, delete]);
        Assert.True(plan.Plan.Success, plan.Plan.Error?.Message);
        Assert.Equal("rename", plan.Plan.Items[1].DependsOn);
        Assert.Equal("T", Assert.Single(f.Requests, r => r.Method == "read_cross_references").CrossReferenceSelector!.Hmi!.TagName);
        f.Put("hmi_get_tag|U", new HmiTagDetailInfo { Tag = Tag("U") });
        var start = f.Requests.Length;
        var replan = await planner.ReplanAsync(delete);
        Assert.True(replan.Replan.Success, replan.Replan.Error?.Message);
        var reads = f.Requests.Skip(start).ToArray();
        Assert.DoesNotContain(reads, r => r.Method == "hmi_list_tags");
        Assert.Equal("U", Assert.Single(reads, r => r.Method == "read_cross_references").CrossReferenceSelector!.Hmi!.TagName);
    }

    [Fact]
    public async Task CreatedDeletedAndRecreatedLineagesDoNotQueryNewTags()
    {
        using var f = await Fixture.Create();
        var create = Item("create_tag", "create"); create.TableName = "Table"; create.DataType = "Bool";
        var rename = Item("update_tag", "rename"); rename.NewName = "U";
        var result = await new HmiWritePlanner(f.Client).PlanAsync([Item("delete_tag", "old"), create, rename, Item("delete_tag", "new", "U")]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Single(f.Requests, r => r.Method == "read_cross_references");
        Assert.Equal("rename", result.Plan.Items[3].DependsOn);
    }

    [Fact]
    public async Task DeletionIncludesUsedByAndMemberAlarmReferences()
    {
        using var f = await Fixture.Create();
        f.Put("hmi_list_alarms", new HmiAlarmListInfo { Page = new(0, 2000, 1, null), Alarms = [new() { Name = "Alarm", Kind = "discrete", TriggerTag = "T.Member" }] });
        f.Put("read_cross_references", References("T", "Screen", "HmiScreen"));
        var result = await new HmiWritePlanner(f.Client).PlanAsync([Item("delete_tag")]);
        var refs = result.Plan.Items[0].Effect!.Removes!.References;
        Assert.Contains(refs, r => r.Name == "Screen"); Assert.Contains(refs, r => r.Name == "Alarm");
        Assert.Contains(result.Guards["a"], g => g.Id == "hmi_deletes_referenced_tag");
    }

    [Fact]
    public async Task MoreThan200OriginalDeletedTagsBlocks()
    {
        using var f = await Fixture.Create();
        f.Put("hmi_list_tags", new HmiTagListInfo { Page = new(0, 2000, 201, null), Tags = Enumerable.Range(0, 201).Select(i => Tag("T" + i)).ToList() });
        var item = new HmiWriteOperationRequest { OperationId = "a", Operation = "delete_tag_table", HmiName = "H", TableName = "Table" };
        var result = await new HmiWritePlanner(f.Client).PlanAsync([item]);
        Assert.Contains(result.Guards["a"], g => g.Id == "hmi_state_unverifiable");
        Assert.True(f.Requests.Count(r => r.Method == "read_cross_references") <= 200);
    }

    [Fact]
    public async Task ReplanTableDeletePagesOnlyItsOwnTagsAndRejectsUncoveredTags()
    {
        using var f = await Fixture.Create();
        var delete = new HmiWriteOperationRequest { OperationId = "delete", Operation = "delete_tag_table", HmiName = "H", TableName = "Table" };
        var planner = new HmiWritePlanner(f.Client);
        var initial = await planner.PlanAsync([Item(), delete]);
        Assert.True(initial.Plan.Success, initial.Plan.Error?.Message);
        f.Put("hmi_list_tags", new HmiTagListInfo { Tags = [Tag("T")], Page = new(0, 2000, 2, 1) { SnapshotId = "scoped" } });
        f.Put("hmi_list_tags|1", new HmiTagListInfo { Tags = [Tag("Unexpected")], Page = new(1, 2000, 2, null) });
        var start = f.Requests.Length;
        var result = await planner.ReplanAsync(delete);
        Assert.Contains(result.Guards, g => g.Id == "hmi_state_unverifiable");
        var scoped = f.Requests.Skip(start).Where(r => r.Method == "hmi_list_tags").ToArray();
        Assert.Equal(2, scoped.Length);
        Assert.All(scoped, r => { Assert.Equal("Table", r.HmiQuery!.TableName); Assert.Equal(2000, r.HmiQuery.Limit); });
        Assert.Equal("scoped", scoped[1].HmiQuery!.SnapshotId);
        Assert.DoesNotContain(f.Requests.Skip(start), r => r.Method == "read_cross_references");
    }

    [Fact]
    public async Task ReplanMissIsTargetNotFound()
    {
        using var f = await Fixture.Create();
        var item = Item("update_tag"); item.NewName = "U";
        var planner = new HmiWritePlanner(f.Client);
        Assert.True((await planner.PlanAsync([item])).Plan.Success);
        f.Remove("hmi_get_tag|T");
        Assert.Equal("target_not_found", (await planner.ReplanAsync(item)).Replan.Error!.Category);
    }

    [Fact]
    public async Task CreatedThenRenamedDeletedTagNeedsNoCrossReferenceQuery()
    {
        using var f = await Fixture.Create();
        var create = Item("create_tag", "create", "U"); create.TableName = "Table";
        var rename = Item("update_tag", "rename", "U"); rename.NewName = "V";
        var result = await new HmiWritePlanner(f.Client).PlanAsync([create, rename, Item("delete_tag", "delete", "V")]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.DoesNotContain(f.Requests, r => r.Method == "read_cross_references");
    }

    [Fact]
    public async Task RetargetedObservedAlarmDoesNotLoseUncertainReferenceEvidence()
    {
        using var f = await Fixture.Create();
        f.Put("hmi_list_tags", new HmiTagListInfo { Tags = [Tag("T"), Tag("U")], Page = new(0, 2000, 2, null) });
        f.Put("hmi_list_alarms", new HmiAlarmListInfo { Alarms = [new() { Name = "Existing", Kind = "discrete", TriggerTag = "T" }], Page = new(0, 2000, 1, null) });
        f.Put("read_cross_references", References("T", "Existing", "HmiDiscreteAlarm"));
        var alarm = Alarm("retarget", "U"); alarm.Operation = "update_alarm"; alarm.AlarmName = "Existing";
        var result = await new HmiWritePlanner(f.Client).PlanAsync([alarm, Item("delete_tag", "delete")]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Contains(result.Guards["delete"], g => g.Id == "hmi_state_unverifiable");
    }

    [Theory]
    [InlineData("nullTags")]
    [InlineData("nullRow")]
    [InlineData("nullPage")]
    public async Task NullRequiredEvidenceIsProtocolError(string fault)
    {
        using var f = await Fixture.Create();
        var payload = System.Text.Json.Nodes.JsonNode.Parse(WorkerJson.SerializePayload(new HmiTagListInfo { Tags = [Tag("T")], Page = new(0, 2000, 1, null) }))!;
        if (fault == "nullTags") payload["tags"] = null;
        if (fault == "nullRow") payload["tags"]![0] = null;
        if (fault == "nullPage") payload["page"] = null;
        f.PutRaw("hmi_list_tags", payload.ToJsonString());
        Assert.Equal("protocol_error", (await new HmiWritePlanner(f.Client).PlanAsync([Item()])).Plan.Error!.Category);
    }

    [Fact]
    public async Task LaterDeletionEvidenceAlsoInformsEarlierAlarmDependency()
    {
        using var f = await Fixture.Create();
        f.Put("hmi_list_alarms", new HmiAlarmListInfo { Alarms = [new() { Name = "Existing", Kind = "discrete", TriggerTag = "Elsewhere" }], Page = new(0, 2000, 1, null) });
        f.Put("read_cross_references", References("T", "Existing", "HmiDiscreteAlarm"));
        var rename = Item("update_tag", "rename"); rename.NewName = "U";
        var alarm = new HmiWriteOperationRequest { OperationId = "alarm", Operation = "update_alarm", HmiName = "H", AlarmName = "Existing", AlarmKind = "discrete", Priority = 7 };
        var result = await new HmiWritePlanner(f.Client).PlanAsync([rename, alarm, Item("delete_tag", "delete", "U")]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal("rename", result.Plan.Items[1].DependsOn);
    }

    [Fact]
    public async Task FreshDeleteReplanPagesMemberTriggerAlarmsOfBothKinds()
    {
        using var f = await Fixture.Create();
        var delete = Item("delete_tag", "delete");
        var planner = new HmiWritePlanner(f.Client);
        Assert.True((await planner.PlanAsync([Item(), delete])).Plan.Success);
        f.Put("hmi_list_alarms", new HmiAlarmListInfo { Page = new(0, 2000, 2, 1) { SnapshotId = "alarms" }, Alarms = [new() { Name = "Discrete", Kind = "discrete", TriggerTag = "T.Member" }] });
        f.Put("hmi_list_alarms|1", new HmiAlarmListInfo { Page = new(1, 2000, 2, null), Alarms = [new() { Name = "Analog", Kind = "analog", TriggerTag = "T.Member.Nested" }] });
        var start = f.Requests.Length;
        var result = await planner.ReplanAsync(delete);
        Assert.True(result.Replan.Success, result.Replan.Error?.Message);
        Assert.Equal(2, result.Replan.Plan!.Effect!.Removes!.References.Count);
        Assert.Contains(result.Guards, g => g.Id == "hmi_deletes_referenced_tag");
        var pages = f.Requests.Skip(start).Where(r => r.Method == "hmi_list_alarms").ToArray();
        Assert.Equal(2, pages.Length);
        Assert.All(pages, p => { Assert.Equal("H", p.HmiQuery!.HmiName); Assert.Equal(2000, p.HmiQuery.Limit); });
        Assert.Equal("alarms", pages[1].HmiQuery!.SnapshotId);
    }

    [Fact]
    public async Task Exactly200PlanQueriesRemainCoveredDuringDependentReplan()
    {
        using var f = await Fixture.Create();
        f.Put("hmi_list_tags", new HmiTagListInfo { Page = new(0, 2000, 200, null), Tags = Enumerable.Range(0, 200).Select(i => Tag("T" + i)).ToList() });
        var delete = new HmiWriteOperationRequest { OperationId = "delete", Operation = "delete_tag_table", HmiName = "H", TableName = "Table" };
        var planner = new HmiWritePlanner(f.Client);
        var result = await planner.PlanAsync([Item(tag: "T0"), delete]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.DoesNotContain(result.Guards["delete"], g => g.Id == "hmi_state_unverifiable");
        Assert.Equal(200, f.Requests.Count(r => r.Method == "read_cross_references"));
        var late = await planner.ReplanAsync(delete);
        Assert.True(late.Replan.Success, late.Replan.Error?.Message);
        Assert.DoesNotContain(late.Guards, g => g.Id == "hmi_state_unverifiable");
        Assert.Equal(400, f.Requests.Count(r => r.Method == "read_cross_references"));
    }

    [Fact]
    public async Task RenamedParentAndTableKeepOriginalMemberProofAndDependencies()
    {
        using var f = await Fixture.Create();
        f.Put("hmi_get_tag|T", new HmiTagDetailInfo { Tag = Tag("T"), Members = [new() { Tag = Tag("T.Member"), Members = [new() { Tag = Tag("T.Member.Nested") }] }] });
        var table = new HmiWriteOperationRequest { OperationId = "table", Operation = "update_tag_table", HmiName = "H", TableName = "Table", NewName = "RenamedTable" };
        var rename = Item("update_tag", "rename"); rename.NewName = "U";
        var result = await new HmiWritePlanner(f.Client).PlanAsync([table, rename, Alarm("alarm", "U.Member.Nested")]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal("table", result.Plan.Items[1].DependsOn);
        Assert.Equal("RenamedTable", result.Plan.Items[1].Effect!.Target.TableName);
        Assert.Equal("rename", result.Plan.Items[2].DependsOn);
        Assert.Equal("T", Assert.Single(f.Requests, r => r.Method == "hmi_get_tag").HmiQuery!.TagName);
        Assert.Contains(result.Plan.Items[2].Effect!.Changes, c => c.Field == "triggerBitAddress" && c.Derived);
    }

    [Fact]
    public async Task IncompleteCrossReferencesBlockDeletion()
    {
        using var f = await Fixture.Create();
        var refs = References("T"); refs.IsComplete = false;
        f.Put("read_cross_references", refs);
        var result = await new HmiWritePlanner(f.Client).PlanAsync([Item("delete_tag")]);
        Assert.Contains(result.Guards["a"], g => g.Id == "hmi_state_unverifiable");
    }

    private static HmiTagRowInfo Tag(string name) => new() { Name = name, Table = "Table", Connection = "<Internal tag>" };
    private static HmiWriteOperationRequest Alarm(string id, string trigger) => new() { OperationId = id, Operation = "create_alarm", HmiName = "H", AlarmKind = "discrete", AlarmName = "NewAlarm", TriggerTag = trigger, AlarmClass = "Class", BitNumber = 0 };
    private static CrossReferenceReport References(string tag, string? name = null, string type = "HmiScreen") => new()
    {
        IsComplete = true, OwnerQueryCount = 1, SuccessfulOwnerQueryCount = 1,
        Target = new() { Hmi = new() { HmiName = "H", TagName = tag } },
        Sources = name is null ? [] : [new() { Name = tag, References = [new() { Name = name, TypeName = type, Path = "path", Locations = [new() { ReferenceType = "UsedBy", Access = "Read" }] }] }]
    };

    // A scripted stdio peer exercises the real client without loading or launching Siemens code.
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "hmi-planner-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, string> payloads = new();
        private readonly ProjectSessionBinding binding;
        public OpennessWorkerClient Client { get; }
        public WorkerRequest[] Requests => File.ReadAllLines(Path.Combine(directory, "requests")).Select(l => JsonSerializer.Deserialize<WorkerRequest>(l, WorkerJson.Request)!).Where(r => r.Method != "get_project_status").ToArray();
        private Fixture()
        {
            Directory.CreateDirectory(directory);
            binding = new ProjectSessionBinding(null); Client = new(binding);
            var script = """
                param([string]$Root)
                while (($line = [Console]::In.ReadLine()) -ne $null) {
                    $r = $line | ConvertFrom-Json
                    [IO.File]::AppendAllText((Join-Path $Root 'requests'), $line + [Environment]::NewLine)
                    $envelope = Get-Content -Raw (Join-Path $Root 'envelope') | ConvertFrom-Json
                    if ($r.method -eq 'hello') { $envelope = Get-Content -Raw (Join-Path $Root 'hello') | ConvertFrom-Json }
                    elseif ($r.method -ne 'get_project_status') {
                        $data = Get-Content -Raw (Join-Path $Root 'payloads') | ConvertFrom-Json
                        $key = $r.method
                        if ($r.hmiQuery.tagName) { $key += '|' + $r.hmiQuery.tagName }
                        elseif ($r.hmiQuery.alarmName) { $key += '|' + $r.hmiQuery.alarmName }
                        elseif ($r.hmiQuery.offset -gt 0) { $key += '|' + $r.hmiQuery.offset }
                        $value = $data.PSObject.Properties[$key]
                        if (!$value) { $value = $data.PSObject.Properties[$r.method] }
                        if ($value) { $envelope.payload = $value.Value }
                        else { $envelope.success = $false; $envelope | Add-Member failureCategory 'target_not_found'; $envelope | Add-Member error 'No fixture target' }
                    }
                    [Console]::Out.WriteLine(($envelope | ConvertTo-Json -Compress -Depth 30)); [Console]::Out.Flush()
                }
                """;
            File.WriteAllText(Path.Combine(directory, "worker.ps1"), script);
            var identity = new WorkerSessionIdentity { WorkerSessionId = "planner", SessionGeneration = 1, PortalProcessId = 4242, ProjectPath = Path.Combine(directory, "fixture.ap21") };
            File.WriteAllText(Path.Combine(directory, "envelope"), JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = "{}", SessionIdentity = identity, ResolvedProjectPath = identity.ProjectPath }, WorkerJson.Envelope));
            File.WriteAllText(Path.Combine(directory, "hello"), JsonSerializer.Serialize(new WorkerResponse { Success = true, ProtocolVersion = WorkerProtocol.Version, Capabilities = WorkerProtocol.RequiredCapabilities.ToList() }, WorkerJson.Envelope));
            var transport = new PersistentWorkerTransport(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), TimeSpan.FromSeconds(15), workerArgs: $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{Path.Combine(directory, "worker.ps1")}\" -Root \"{directory}\"");
            typeof(OpennessWorkerClient).GetField("_transport", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Client, transport);
            Put("hmi_list_hmi_devices", new HmiDeviceListInfo { Devices = [new() { DeviceName = "Device", SoftwareName = "H", Kind = "unified" }] });
            Put("hmi_list_project_languages", new HmiProjectLanguagesInfo { Languages = ["en-US"] });
            Put("hmi_list_tag_tables", new HmiTagTableTreeInfo { Tables = [new() { Name = "Table", TagCount = 1 }] });
            Put("hmi_list_tags", new HmiTagListInfo { Tags = [Tag("T")], Page = new(0, 2000, 1, null) });
            Put("hmi_get_tag|T", new HmiTagDetailInfo { Tag = Tag("T") });
            Put("hmi_list_connections", new HmiConnectionListInfo { Connections = [new() { Name = "Connection" }] });
            Put("hmi_list_alarm_classes", new HmiAlarmClassesInfo { AlarmClasses = [new() { Name = "Class" }] });
            Put("hmi_list_alarms", new HmiAlarmListInfo { Page = new(0, 2000, 0, null) });
            Put("read_cross_references", References("T"));
        }
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); var status = await f.Client.GetProjectStatusAsync(null);
            Assert.True(status.Success, status.Error);
            Assert.True(f.binding.BindVerified(status.SessionIdentity!, forceRebind: false, out var bindError), bindError); return f;
        }
        public void Put<T>(string key, T payload) => PutRaw(key, WorkerJson.SerializePayload(payload));
        public void PutRaw(string key, string payload) { payloads[key] = payload; File.WriteAllText(Path.Combine(directory, "payloads"), JsonSerializer.Serialize(payloads)); }
        public void Remove(string key) { payloads.Remove(key); File.WriteAllText(Path.Combine(directory, "payloads"), JsonSerializer.Serialize(payloads)); }
        public void Dispose() { Client.Dispose(); Directory.Delete(directory, true); }
    }
}
