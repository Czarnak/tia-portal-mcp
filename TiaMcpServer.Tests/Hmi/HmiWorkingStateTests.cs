using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Hmi;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

public class HmiWorkingStateTests
{
    private static HmiEvidence Evidence() => new()
    {
        HmiName = "HMI", Tables = new[] { new HmiTableEvidence("Table", null), new HmiTableEvidence("Other", "Group") },
        Groups = new[] { "Group" }, Connections = new[] { "PLC" }, AlarmClasses = new[] { "Warning" }, Cultures = new[] { "en-US" },
        Tags = new[] { new HmiTagRowInfo { Name = "T", Table = "Table", Connection = "<Internal tag>", DataType = "Bool" } },
        Alarms = new[] { new HmiAlarmRowInfo { Name = "Alarm", Kind = "discrete", TriggerTag = "T.Member" } },
        MemberPaths = new[] { "T.Member" }, References = new[] { new HmiReference("T", "Screen", "HmiScreen", "/Screen") }, Complete = true
    };
    private static HmiWriteOperationRequest Item(string operation, string id = "item") => new() { OperationId = id, Operation = operation, HmiName = "HMI" };
    [Theory]
    [InlineData("create_tag", "t")]
    [InlineData("create_tag_table", "other")]
    [InlineData("create_alarm", "alarm")]
    public void Collision_IsCaseInsensitiveAndScoped(string operation, string name)
    {
        var item = Item(operation); item.TagName = name; item.TableName = operation == "create_tag_table" ? name : "Other";
        item.AlarmName = name; item.AlarmKind = "discrete"; item.TriggerTag = "T";
        Assert.Contains(HmiWorkingState.Create(Evidence()).Resolve(item).Guards, g => g.Id == "hmi_name_collision");
    }
    [Fact] public void AlarmNames_AreScopedByKind()
    {
        var item = Item("create_alarm"); item.AlarmName = "Alarm"; item.AlarmKind = "analog"; item.TriggerTag = "T";
        Assert.Empty(HmiWorkingState.Create(Evidence()).Resolve(item).Guards);
    }
    [Fact] public void CaseOnlyRename_DoesNotCollideWithItself()
    {
        var item = Item("update_tag"); item.TagName = "T"; item.NewName = "t";
        Assert.Empty(HmiWorkingState.Create(Evidence()).Resolve(item).Guards);
    }
    [Fact] public void DeleteThenCreateCaseVariant_DoesNotCollide()
    {
        var state = HmiWorkingState.Create(Evidence()); var delete = Item("delete_tag"); delete.TagName = "T"; state.Apply(delete);
        var create = Item("create_tag"); create.TagName = "t"; create.TableName = "Table";
        Assert.DoesNotContain(state.Resolve(create).Guards, g => g.Id == "hmi_name_collision");
    }
    [Fact] public void TableRename_UpdatesMembershipAndDependency()
    {
        var evidence = Evidence(); var state = HmiWorkingState.Create(evidence);
        var rename = Item("update_tag_table", "rename"); rename.TableName = "Table"; rename.NewName = "Renamed"; state.Apply(rename);
        var create = Item("create_tag"); create.TableName = "Renamed"; create.TagName = "New";
        Assert.Equal("rename", state.Resolve(create).Effect!.DependsOn);
        var delete = Item("delete_tag_table"); delete.TableName = "Renamed";
        Assert.Equal(new[] { "T" }, state.Resolve(delete).Effect!.Removes!.Tags);
        Assert.Equal("Table", evidence.Tags[0].Table);
    }
    [Theory] [InlineData("connection")] [InlineData("alarmClass")] [InlineData("triggerTag")]
    public void MissingReference_ReturnsTargetNotFound(string field)
    {
        var item = Item(field == "connection" ? "create_tag" : "create_alarm"); item.TagName = "New"; item.TableName = "Table";
        item.AlarmName = "New"; item.AlarmKind = "discrete"; item.TriggerTag = "T";
        if (field == "connection") item.Connection = "Missing";
        if (field == "alarmClass") item.AlarmClass = "Missing";
        if (field == "triggerTag") item.TriggerTag = "Missing";
        Assert.Equal("target_not_found", HmiWorkingState.Create(Evidence()).Resolve(item).Error!.Category);
    }
    [Fact] public void InternalTag_PlcTagOnlyUpdateFails()
    {
        var item = Item("update_tag"); item.TagName = "T"; item.PlcTag = "DB.Value";
        Assert.Equal("validation_error", HmiWorkingState.Create(Evidence()).Resolve(item).Error!.Category);
    }
    [Fact] public void Rename_PropagatesMemberTriggersAndExternalReferencesWithoutChangingEvidence()
    {
        var evidence = Evidence(); var state = HmiWorkingState.Create(evidence);
        var rename = Item("update_tag", "rename"); rename.TagName = "T"; rename.NewName = "Renamed"; state.Apply(rename);
        var delete = Item("delete_tag"); delete.TagName = "Renamed";
        var result = state.Resolve(delete);
        Assert.Equal("rename", result.Effect!.DependsOn);
        Assert.Equal(2, result.Effect.Removes!.References.Count);
        Assert.All(result.Effect.Removes.References, r => Assert.Equal("Renamed", r.Tag));
        Assert.Equal("T.Member", evidence.Alarms[0].TriggerTag);
        var alarm = Item("create_alarm"); alarm.AlarmName = "New"; alarm.AlarmKind = "analog"; alarm.TriggerTag = "Renamed.Member";
        Assert.Null(state.Resolve(alarm).Error);
    }
    [Fact] public void DeleteTable_ListsTagsAndReferences()
    {
        var item = Item("delete_tag_table"); item.TableName = "Table";
        var result = HmiWorkingState.Create(Evidence()).Resolve(item);
        Assert.Equal(new[] { "T" }, result.Effect!.Removes!.Tags);
        Assert.Equal(2, result.Effect.Removes.References.Count);
        Assert.Contains(result.Guards, g => g.Id == "hmi_deletes_table_contents");
        Assert.Contains(result.Guards, g => g.Id == "hmi_deletes_referenced_tag");
    }
    [Fact] public void IncompleteEvidence_BlocksInsteadOfClaimingAbsence()
    {
        var evidence = Evidence() with { Complete = false }; var item = Item("delete_tag"); item.TagName = "Missing";
        var result = HmiWorkingState.Create(evidence).Resolve(item);
        Assert.Null(result.Error); Assert.Contains(result.Guards, g => g.Id == "hmi_state_unverifiable");
    }
    [Fact] public void AlarmEffects_WrapEventTextAndPreserveOtherCultures()
    {
        var state = HmiWorkingState.Create(Evidence()); var item = Item("update_alarm", "edit");
        item.AlarmName = "Alarm"; item.AlarmKind = "discrete"; item.EventText = new() { new("en-US", "A & B") };
        Assert.Equal("<body><p>A &amp; B</p></body>", System.Text.Json.JsonSerializer.Deserialize<List<HmiText>>(state.Resolve(item).Effect!.Changes.Single(c => c.Field == "eventText").Requested!)![0].Text);
        state.Apply(item); item.EventText = new() { new("en-US", "New") };
        Assert.Equal("<body><p>A &amp; B</p></body>", System.Text.Json.JsonSerializer.Deserialize<List<HmiText>>(state.Resolve(item).Effect!.Changes.Single(c => c.Field == "eventText").Current!)![0].Text);
    }
    [Fact] public void UnknownConnection_BlocksInternalDataTypeDecision()
    {
        var evidence = Evidence(); evidence.Tags[0].Connection = null;
        var item = Item("update_tag"); item.TagName = "T"; item.DataType = "Int";
        Assert.Contains(HmiWorkingState.Create(evidence).Resolve(item).Guards, g => g.Id == "hmi_state_unverifiable");
    }
    [Fact] public void RemovingAlarm_RemovesItsObservedReference()
    {
        var evidence = Evidence() with { References = new[] { new HmiReference("T", "Alarm", "HmiDiscreteAlarm", null) } };
        var state = HmiWorkingState.Create(evidence); var alarm = Item("delete_alarm"); alarm.AlarmName = "Alarm"; alarm.AlarmKind = "discrete"; state.Apply(alarm);
        var tag = Item("delete_tag"); tag.TagName = "T";
        Assert.Empty(state.Resolve(tag).Effect!.Removes!.References);
    }
    [Fact] public void CreateUpdateDelete_PreservesFieldsAndLatestDependency()
    {
        var state = HmiWorkingState.Create(Evidence()); var table = Item("create_tag_table", "table"); table.TableName = "NewTable"; table.GroupPath = "Group"; state.Apply(table);
        var tag = Item("create_tag", "tag"); tag.TagName = "NewTag"; tag.TableName = "NewTable"; tag.Connection = "PLC"; tag.PlcTag = "DB.Value";
        Assert.Equal("table", state.Resolve(tag).Effect!.DependsOn); Assert.Contains(state.Resolve(tag).Effect!.Changes, c => c.Field == "dataType" && c.Derived); state.Apply(tag);
        var alarm = Item("create_alarm", "alarm"); alarm.AlarmName = "NewAlarm"; alarm.AlarmKind = "analog"; alarm.TriggerTag = "NewTag"; alarm.Condition = "Equal"; alarm.ConditionValue = System.Text.Json.JsonSerializer.SerializeToElement(50.0); alarm.AlarmClass = "Warning"; alarm.Priority = 4;
        Assert.Equal("tag", state.Resolve(alarm).Effect!.DependsOn); state.Apply(alarm);
        alarm.Operation = "update_alarm"; alarm.OperationId = "rename"; alarm.NewName = "RenamedAlarm"; alarm.Priority = 5;
        Assert.Equal("4", state.Resolve(alarm).Effect!.Changes.Single(c => c.Field == "priority").Current); state.Apply(alarm);
        var delete = Item("delete_alarm"); delete.AlarmName = "RenamedAlarm"; delete.AlarmKind = "analog";
        Assert.Equal("rename", state.Resolve(delete).Effect!.DependsOn); state.Apply(delete);
        Assert.Equal("target_not_found", state.Resolve(delete).Error!.Category);
        var deleteTable = Item("delete_tag_table"); deleteTable.TableName = "NewTable"; state.Apply(deleteTable);
        var missingTag = Item("delete_tag"); missingTag.TagName = "NewTag";
        Assert.Equal("target_not_found", state.Resolve(missingTag).Error!.Category);
    }
    [Fact] public void BoundDataTypeUpdate_IsRejectedAndMissingGroupCultureAndMemberAreRejected()
    {
        var evidence = Evidence(); evidence.Tags[0].Connection = "PLC"; var state = HmiWorkingState.Create(evidence);
        var tag = Item("update_tag"); tag.TagName = "T"; tag.DataType = "Int";
        Assert.Equal("validation_error", state.Resolve(tag).Error!.Category);
        tag.DataType = null; tag.Comment = new() { new("de-DE", "Text") };
        Assert.Equal("target_not_found", state.Resolve(tag).Error!.Category);
        var table = Item("create_tag_table"); table.TableName = "New"; table.GroupPath = "Missing";
        Assert.Equal("target_not_found", state.Resolve(table).Error!.Category);
        var alarm = Item("create_alarm"); alarm.AlarmName = "New"; alarm.AlarmKind = "analog"; alarm.TriggerTag = "T.Missing";
        Assert.Equal("target_not_found", state.Resolve(alarm).Error!.Category);
    }
    [Fact] public void AmbiguousAndBlockedOperations_DoNotMutateState()
    {
        var evidence = Evidence(); var state = HmiWorkingState.Create(evidence with { Tags = new[] { evidence.Tags[0], evidence.Tags[0] } });
        var delete = Item("delete_tag"); delete.TagName = "T";
        Assert.Equal("target_ambiguous", state.Resolve(delete).Error!.Category); state.Apply(delete);
        state = HmiWorkingState.Create(evidence); var create = Item("create_tag"); create.TagName = "T"; create.TableName = "Other"; state.Apply(create);
        Assert.Null(state.Resolve(delete).Error);
        Assert.Null(state.LastTouchedBy(state.Resolve(delete).Effect!.Target));
    }}
