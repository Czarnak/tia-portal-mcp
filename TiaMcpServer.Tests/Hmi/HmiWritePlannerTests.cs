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
        f.Put("hmi_list_alarms", new HmiAlarmListInfo { Page = new(0, 2000, 2, 1) { SnapshotId = "alarms" }, Alarms = [new() { Name = "A", Kind = "discrete" }] });
        f.Put("hmi_list_alarms|1", new HmiAlarmListInfo { Page = new(1, 2000, 2, null), Alarms = [new() { Name = "B", Kind = "analog" }] });
        var second = new HmiWriteOperationRequest { OperationId = "b", Operation = "delete_tag_table", HmiName = "Device", TableName = "Table" };
        var planner = new HmiWritePlanner(f.Client);
        var result = await planner.PlanAsync([Item(), second]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal("H", planner.ResolvedHmiName("b"));
        Assert.Equal(1, f.Requests.Count(r => r.Method == "hmi_list_tag_tables"));
        Assert.Equal(new[] { "T", "U" }, result.Plan.Items[1].Effect!.Removes!.Tags);
        foreach (var (method, snapshot) in new[] { ("hmi_list_tags", "tags"), ("hmi_list_alarms", "alarms") })
        {
            var pages = f.Requests.Where(r => r.Method == method).ToArray();
            Assert.Equal(2, pages.Length); Assert.All(pages, p => Assert.Equal(2000, p.HmiQuery!.Limit));
            Assert.Equal(snapshot, pages[1].HmiQuery!.SnapshotId);
        }
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
        var result = await new HmiWritePlanner(f.Client).PlanAsync([TableDelete()]);
        if (fault == "missing") Assert.Equal("protocol_error", result.Plan.Error!.Category);
        else Assert.Contains(result.Guards["delete"], g => g.Id == "hmi_state_unverifiable");
    }

    [Theory]
    [InlineData("get_tag")]
    [InlineData("get_alarm")]
    public async Task IncompleteTargetReadBlocks(string read)
    {
        using var f = await Fixture.Create();
        if (read == "get_tag") f.Put("hmi_get_tag|T", new HmiTagDetailInfo { Tag = Tag("T"), IsComplete = false });
        else f.Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "discrete" }, IsComplete = false });
        var item = read == "get_tag" ? Item() : new HmiWriteOperationRequest { OperationId = "a", Operation = "update_alarm", HmiName = "H", AlarmName = "A", AlarmKind = "discrete", Priority = 1 };
        var result = await new HmiWritePlanner(f.Client).PlanAsync([item]);
        Assert.Contains(result.Guards["a"], g => g.Id == "hmi_state_unverifiable");
    }

    [Fact]
    public async Task IncompleteEvidenceWithAbsentDeleteTargetBlocks()
    {
        using var f = await Fixture.Create();
        f.Put("hmi_list_tag_tables", new HmiTagTableTreeInfo { Tables = [new() { Name = "Table", TagCount = 1 }], IsComplete = false });
        var result = await new HmiWritePlanner(f.Client).PlanAsync([Item("delete_tag", "a", "Missing")]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Contains(result.Guards["a"], g => g.Id == "hmi_state_unverifiable");
        Assert.DoesNotContain(f.Requests, r => r.Method == "read_cross_references");
    }

    [Fact]
    public async Task UnresolvableTarget_FailsBeforeAnyConsequenceRead()
    {
        using var f = await Fixture.Create();
        var result = await new HmiWritePlanner(f.Client).PlanAsync([Item("update_tag", "a", "Missing"), Item("delete_tag", "delete")]);
        Assert.False(result.Plan.Success);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, result.Plan.Error!.Category);
        Assert.DoesNotContain(f.Requests, r => r.Method is "read_cross_references" or "hmi_list_alarms");
    }

    [Fact]
    public async Task RenamedExistingTagQueriesOriginalName()
    {
        using var f = await Fixture.Create();
        var rename = Item("update_tag", "rename"); rename.NewName = "U";
        var plan = await new HmiWritePlanner(f.Client).PlanAsync([rename, Item("delete_tag", "delete", "U")]);
        Assert.True(plan.Plan.Success, plan.Plan.Error?.Message);
        Assert.Equal("rename", plan.Plan.Items[1].Effect!.DependsOn);
        Assert.Equal("T", Assert.Single(f.Requests, r => r.Method == "read_cross_references").CrossReferenceSelector!.Hmi!.TagName);
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
        Assert.Equal("rename", result.Plan.Items[3].Effect!.DependsOn);
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
    public async Task TableDeletePagesOnlyItsOwnTags()
    {
        using var f = await Fixture.Create();
        f.Put("hmi_list_tags", new HmiTagListInfo { Tags = [Tag("T")], Page = new(0, 2000, 2, 1) { SnapshotId = "scoped" } });
        f.Put("hmi_list_tags|1", new HmiTagListInfo { Tags = [Tag("V")], Page = new(1, 2000, 2, null) });
        var result = await new HmiWritePlanner(f.Client).PlanAsync([Item(), TableDelete()]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        var scoped = f.Requests.Where(r => r.Method == "hmi_list_tags").ToArray();
        Assert.Equal(2, scoped.Length);
        Assert.All(scoped, r => { Assert.Equal("Table", r.HmiQuery!.TableName); Assert.Equal(2000, r.HmiQuery.Limit); });
        Assert.Equal("scoped", scoped[1].HmiQuery!.SnapshotId);
        Assert.Equal(new[] { "T", "V" }, f.Requests.Where(r => r.Method == "read_cross_references").Select(r => r.CrossReferenceSelector!.Hmi!.TagName));
    }

    [Theory]
    [InlineData("update_tag")]
    [InlineData("delete_tag")]
    [InlineData("update_alarm")]
    public async Task AbsentTargetFailsAtPlan(string operation)
    {
        using var f = await Fixture.Create();
        f.Remove("hmi_get_tag|T");
        var item = operation == "update_alarm"
            ? new HmiWriteOperationRequest { OperationId = "a", Operation = operation, HmiName = "H", AlarmName = "Missing", AlarmKind = "discrete", Priority = 1 }
            : Item(operation);
        Assert.Equal("target_not_found", (await new HmiWritePlanner(f.Client).PlanAsync([item])).Plan.Error!.Category);
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
        f.Put("hmi_get_alarm|Existing", new HmiAlarmDetailInfo { Alarm = new() { Name = "Existing", Kind = "discrete", TriggerTag = "T" } });
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
        Assert.Equal("protocol_error", (await new HmiWritePlanner(f.Client).PlanAsync([TableDelete()])).Plan.Error!.Category);
    }

    [Fact]
    public async Task LaterDeletionEvidenceAlsoInformsEarlierAlarmDependency()
    {
        using var f = await Fixture.Create();
        f.Put("hmi_list_alarms", new HmiAlarmListInfo { Alarms = [new() { Name = "Existing", Kind = "discrete", TriggerTag = "Elsewhere" }], Page = new(0, 2000, 1, null) });
        f.Put("hmi_get_alarm|Existing", new HmiAlarmDetailInfo { Alarm = new() { Name = "Existing", Kind = "discrete", TriggerTag = "Elsewhere" } });
        f.Put("read_cross_references", References("T", "Existing", "HmiDiscreteAlarm"));
        var rename = Item("update_tag", "rename"); rename.NewName = "U";
        var alarm = new HmiWriteOperationRequest { OperationId = "alarm", Operation = "update_alarm", HmiName = "H", AlarmName = "Existing", AlarmKind = "discrete", Priority = 7 };
        var result = await new HmiWritePlanner(f.Client).PlanAsync([rename, alarm, Item("delete_tag", "delete", "U")]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal("rename", result.Plan.Items[1].Effect!.DependsOn);
    }

    [Fact]
    public async Task Exactly200PlanQueriesRemainCovered()
    {
        using var f = await Fixture.Create();
        f.Put("hmi_list_tags", new HmiTagListInfo { Page = new(0, 2000, 200, null), Tags = Enumerable.Range(0, 200).Select(i => Tag("T" + i)).ToList() });
        f.Put("hmi_get_tag|T0", new HmiTagDetailInfo { Tag = Tag("T0") });
        var result = await new HmiWritePlanner(f.Client).PlanAsync([Item(tag: "T0"), TableDelete()]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.DoesNotContain(result.Guards["delete"], g => g.Id == "hmi_state_unverifiable");
        Assert.Equal(200, f.Requests.Count(r => r.Method == "read_cross_references"));
    }

    [Fact]
    public async Task RenamedParentAndTableKeepDependencies()
    {
        using var f = await Fixture.Create();
        var table = new HmiWriteOperationRequest { OperationId = "table", Operation = "update_tag_table", HmiName = "H", TableName = "Table", NewName = "RenamedTable" };
        var rename = Item("update_tag", "rename"); rename.NewName = "U";
        var result = await new HmiWritePlanner(f.Client).PlanAsync([table, rename, Alarm("alarm", "U.Member.Nested")]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal("table", result.Plan.Items[1].Effect!.DependsOn);
        Assert.Equal("RenamedTable", result.Plan.Items[1].Effect!.Target.TableName);
        Assert.Equal("rename", result.Plan.Items[2].Effect!.DependsOn);
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

    [Fact]
    public async Task CreateOnlyCall_ReadsDevicesOnly()
    {
        using var f = await Fixture.Create();
        var table = new HmiWriteOperationRequest { OperationId = "table", Operation = "create_tag_table", HmiName = "H", TableName = "NewTable" };
        var tag = Item("create_tag", "tag", "New"); tag.TableName = "NewTable"; tag.DataType = "Bool"; tag.AcquisitionCycle = null;
        var analog = Alarm("analog", "T.Member.Nested"); analog.AlarmKind = "analog"; analog.AlarmName = "Analog"; analog.BitNumber = null;
        var result = await new HmiWritePlanner(f.Client).PlanAsync([table, tag, Alarm("discrete", "T.Member"), analog]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal(["hmi_list_hmi_devices"], Methods(f));
    }

    [Fact]
    public async Task UpdateCall_ReadsTablesOnceAndEachTargetOnly()
    {
        using var f = await Fixture.Create();
        f.Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "discrete", TriggerTag = "T" } });
        var table = new HmiWriteOperationRequest { OperationId = "table", Operation = "update_tag_table", HmiName = "H", TableName = "Table", NewName = "Renamed" };
        var alarm = new HmiWriteOperationRequest { OperationId = "alarm", Operation = "update_alarm", HmiName = "H", AlarmName = "A", AlarmKind = "discrete", Priority = 3 };
        var result = await new HmiWritePlanner(f.Client).PlanAsync([table, Item(), Item(id: "b"), alarm]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal(["hmi_list_hmi_devices", "hmi_list_tag_tables", "hmi_get_tag", "hmi_get_alarm"], Methods(f));
    }

    [Fact]
    public async Task TwoHmiCall_ReadsEveryTargetBeforeAnyConsequence()
    {
        using var f = await Fixture.Create();
        f.Put("hmi_list_hmi_devices", new HmiDeviceListInfo { Devices = [new() { DeviceName = "Device", SoftwareName = "H", Kind = "unified" }, new() { DeviceName = "Device2", SoftwareName = "H2", Kind = "unified" }] });
        var other = Item("delete_tag", "delete"); other.HmiName = "H2";
        var result = await new HmiWritePlanner(f.Client).PlanAsync([Item(), other]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal(["hmi_list_hmi_devices", "hmi_list_tag_tables", "hmi_get_tag", "hmi_list_tag_tables", "hmi_get_tag", "hmi_list_alarms", "read_cross_references"], Methods(f));
    }

    [Fact]
    public async Task CallWithoutDelete_NeverReadsAlarms()
    {
        using var f = await Fixture.Create();
        var result = await new HmiWritePlanner(f.Client).PlanAsync([Item(), Alarm("alarm", "T")]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.DoesNotContain("hmi_list_alarms", Methods(f));
    }

    [Fact]
    public async Task DeleteTagCall_PagesAlarmsOnceAndQueriesOriginalName()
    {
        using var f = await Fixture.Create();
        f.Put("hmi_list_alarms", new HmiAlarmListInfo { Page = new(0, 2000, 2, 1) { SnapshotId = "alarms" }, Alarms = [new() { Name = "Discrete", Kind = "discrete", TriggerTag = "T.Member" }] });
        f.Put("hmi_list_alarms|1", new HmiAlarmListInfo { Page = new(1, 2000, 2, null), Alarms = [new() { Name = "Analog", Kind = "analog", TriggerTag = "T" }] });
        var rename = Item("update_tag", "rename"); rename.NewName = "U";
        var create = Item("create_tag", "create", "V"); create.TableName = "Table"; create.DataType = "Bool";
        var result = await new HmiWritePlanner(f.Client).PlanAsync([rename, Item("delete_tag", "delete", "U"), create, Item("delete_tag", "deleteNew", "V")]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        var pages = f.Requests.Where(r => r.Method == "hmi_list_alarms").ToArray();
        Assert.Equal(2, pages.Length);
        Assert.All(pages, p => { Assert.Equal("H", p.HmiQuery!.HmiName); Assert.Equal(2000, p.HmiQuery.Limit); });
        Assert.Equal("alarms", pages[1].HmiQuery!.SnapshotId);
        Assert.Equal("T", Assert.Single(f.Requests, r => r.Method == "read_cross_references").CrossReferenceSelector!.Hmi!.TagName);
        Assert.Equal(new[] { "Analog", "Discrete" }, result.Plan.Items[1].Effect!.Removes!.References.Select(r => r.Name).Order());
    }

    [Fact]
    public async Task CreateIntoUnreadTableThenUpdate_ResolvesFromSimulation()
    {
        using var f = await Fixture.Create();
        var create = Item("create_tag", "create", "New"); create.TableName = "Unread"; create.DataType = "Bool";
        var update = Item("update_tag", "update", "New");
        var result = await new HmiWritePlanner(f.Client).PlanAsync([create, update]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal("Unread", result.Plan.Items[1].Effect!.Target.TableName);
        Assert.Empty(result.Guards["update"]);
    }

    [Fact]
    public async Task UpdateOfTagCreatedEarlierInCall_ResolvesFromSimulation()
    {
        using var f = await Fixture.Create();
        var table = new HmiWriteOperationRequest { OperationId = "table", Operation = "create_tag_table", HmiName = "H", TableName = "NewTable" };
        var create = Item("create_tag", "create", "New"); create.TableName = "NewTable"; create.DataType = "Bool";
        var rename = Item("update_tag", "rename", "New"); rename.NewName = "Renamed";
        var result = await new HmiWritePlanner(f.Client).PlanAsync([table, create, rename, Item("update_tag", "update", "Renamed")]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal("Renamed", result.Plan.Items[3].Effect!.Target.Name);
        Assert.Equal("NewTable", result.Plan.Items[3].Effect!.Target.TableName);
    }

    [Fact]
    public async Task DeleteTable_ListsTagsCreatedEarlierInCall()
    {
        using var f = await Fixture.Create();
        var create = Item("create_tag", "create", "New"); create.TableName = "Table"; create.DataType = "Bool";
        var delete = new HmiWriteOperationRequest { OperationId = "delete", Operation = "delete_tag_table", HmiName = "H", TableName = "Table" };
        var result = await new HmiWritePlanner(f.Client).PlanAsync([create, delete]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal(new[] { "New", "T" }, result.Plan.Items[1].Effect!.Removes!.Tags.Order());
        Assert.Equal("Table", Assert.Single(f.Requests, r => r.Method == "hmi_list_tags").HmiQuery!.TableName);
        Assert.Equal("T", Assert.Single(f.Requests, r => r.Method == "read_cross_references").CrossReferenceSelector!.Hmi!.TagName);
    }

    [Fact]
    public async Task PlanDeclaresNoReplan()
    {
        using var f = await Fixture.Create();
        var table = new HmiWriteOperationRequest { OperationId = "table", Operation = "create_tag_table", HmiName = "H", TableName = "NewTable" };
        var tag = Item("create_tag", "tag", "New"); tag.TableName = "NewTable"; tag.DataType = "Bool";
        var result = await new HmiWritePlanner(f.Client).PlanAsync([table, tag, Item("update_tag", "update", "New")]);
        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.All(result.Plan.Items, i => Assert.Null(i.DependsOn));
        Assert.Equal("table", result.Plan.Items[1].Effect!.DependsOn);
        Assert.Equal("tag", result.Plan.Items[2].Effect!.DependsOn);
    }

    private static HmiWriteOperationRequest TableDelete() => new() { OperationId = "delete", Operation = "delete_tag_table", HmiName = "H", TableName = "Table" };
    private static string[] Methods(Fixture f) => f.Requests.Select(r => r.Method).Where(m => m != "hello").ToArray();
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
            Put("hmi_list_tag_tables", new HmiTagTableTreeInfo { Tables = [new() { Name = "Table", TagCount = 1 }] });
            Put("hmi_list_tags", new HmiTagListInfo { Tags = [Tag("T")], Page = new(0, 2000, 1, null) });
            Put("hmi_get_tag|T", new HmiTagDetailInfo { Tag = Tag("T") });
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
