using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Hmi;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

public class HmiWorkingStateTests
{
    private static HmiEvidence Evidence() => new()
    {
        HmiName = "HMI", Tables = new[] { new HmiTableEvidence("Table", null), new HmiTableEvidence("Other", "Group") },
        Tags = new[] { new HmiTagRowInfo { Name = "T", Table = "Table", Connection = "<Internal tag>", DataType = "Bool" } },
        Alarms = new[] { new HmiAlarmRowInfo { Name = "Alarm", Kind = "discrete", TriggerTag = "T.Member" } },
        References = new[] { new HmiReference("T", "Screen", "HmiScreen", "/Screen") }, Complete = true
    };
    private static HmiWriteOperationRequest Item(string operation, string id = "item") => new() { OperationId = id, Operation = operation, HmiName = "HMI" };
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
        var evidence = Evidence();
        evidence.Alarms[0].EventText = new() { new("en-US", "Old"), new("de-DE", "Deutsch") };
        var state = HmiWorkingState.Create(evidence); var item = Item("update_alarm", "edit");
        item.AlarmName = "Alarm"; item.AlarmKind = "discrete"; item.EventText = new() { new("en-US", "A & \"B\" 'C' &quot;") };
        var document = TiaMcpServer.Json.CanonicalJson.ToElement(state.Resolve(item).Effect!);
        var change = document.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("field").GetString() == "eventText[en-US]");
        Assert.Equal(System.Text.Json.JsonValueKind.String, change.GetProperty("requested").ValueKind);
        Assert.Equal("<body><p>A &amp; \"B\" 'C' &amp;quot;</p></body>", change.GetProperty("requested").GetString());
        Assert.Equal("Old", change.GetProperty("current").GetString());
        state.Apply(item); item.EventText = new() { new("en-US", "New"), new("de-DE", "Neu") };
        var changes = state.Resolve(item).Effect!.Changes;
        Assert.Equal("<body><p>A &amp; \"B\" 'C' &amp;quot;</p></body>", changes.Single(c => c.Field == "eventText[en-US]").Current);
        Assert.Equal("Deutsch", changes.Single(c => c.Field == "eventText[de-DE]").Current);
    }
    [Theory] [InlineData("comment")] [InlineData("displayName")] [InlineData("infoText")]
    public void MultilingualEffects_ExposeScalarCultureEntries(string field)
    {
        var item = Item(field == "infoText" ? "update_alarm" : "update_tag"); item.TagName = "T"; item.AlarmName = "Alarm"; item.AlarmKind = "discrete";
        var text = new List<HmiText> { new("en-US", "Plain & text") };
        if (field == "comment") item.Comment = text;
        if (field == "displayName") item.DisplayName = text;
        if (field == "infoText") item.InfoText = text;
        var document = TiaMcpServer.Json.CanonicalJson.ToElement(HmiWorkingState.Create(Evidence()).Resolve(item).Effect!);
        var change = document.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("field").GetString() == field + "[en-US]");
        Assert.Equal("Plain & text", change.GetProperty("requested").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, change.GetProperty("current").ValueKind);
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
    [Fact] public void AmbiguousAndBlockedOperations_DoNotMutateState()
    {
        var evidence = Evidence(); var state = HmiWorkingState.Create(evidence with { Tags = new[] { evidence.Tags[0], evidence.Tags[0] } });
        var delete = Item("delete_tag"); delete.TagName = "T";
        Assert.Equal("target_ambiguous", state.Resolve(delete).Error!.Category); state.Apply(delete);
        Assert.Null(state.LastTouchedBy(new HmiTargetIdentity("Tag", "HMI", "T", null, "Table", null)));
        state = HmiWorkingState.Create(evidence with { Complete = false }); var blocked = Item("delete_tag", "blocked"); blocked.TagName = "T"; state.Apply(blocked);
        Assert.Null(state.LastTouchedBy(new HmiTargetIdentity("Tag", "HMI", "T", null, "Table", null)));
    }
    [Fact] public void CreateTagThenDeleteTable_DependsOnMembershipChange()
    {
        var state = HmiWorkingState.Create(Evidence()); var create = Item("create_tag", "created"); create.TableName = "Table"; create.TagName = "New"; state.Apply(create);
        var delete = Item("delete_tag_table"); delete.TableName = "Table";
        Assert.Equal("created", state.Resolve(delete).Effect!.DependsOn);
        var remove = Item("delete_tag", "removed"); remove.TagName = "New"; state.Apply(remove);
        Assert.Equal("removed", state.Resolve(delete).Effect!.DependsOn);
    }
    [Fact] public void CreateReferencedAlarmThenDeleteTag_DependsOnReferenceChange()
    {
        var state = HmiWorkingState.Create(Evidence()); var create = Item("create_alarm", "created"); create.AlarmName = "NewAlarm"; create.AlarmKind = "discrete"; create.TriggerTag = "T"; state.Apply(create);
        var delete = Item("delete_tag"); delete.TagName = "T";
        Assert.Equal("created", state.Resolve(delete).Effect!.DependsOn);
        create.Operation = "delete_alarm"; create.OperationId = "removed"; create.TriggerTag = null; state.Apply(create);
        Assert.Equal("removed", state.Resolve(delete).Effect!.DependsOn);
    }
    [Fact] public void RenameTagThenUpdateImplicitTriggerAlarm_DependsOnRename()
    {
        var state = HmiWorkingState.Create(Evidence()); var rename = Item("update_tag", "renamed"); rename.TagName = "T"; rename.NewName = "Renamed"; state.Apply(rename);
        var alarm = Item("update_alarm"); alarm.AlarmName = "Alarm"; alarm.AlarmKind = "discrete"; alarm.Priority = 9;
        Assert.Equal("renamed", state.Resolve(alarm).Effect!.DependsOn);
    }
    [Theory] [InlineData("tag")] [InlineData("tag_table")] [InlineData("alarm")]
    public void DeleteThenCreateSameIdentity_DependsOnDelete(string kind)
    {
        var state = HmiWorkingState.Create(Evidence()); var item = Item("delete_" + kind, "deleted"); item.TagName = "T"; item.TableName = "Table"; item.AlarmName = "Alarm"; item.AlarmKind = "discrete"; state.Apply(item);
        item.Operation = "create_" + kind; item.OperationId = "create"; if (kind == "alarm") item.TriggerTag = "T";
        Assert.Equal("deleted", state.Resolve(item).Effect!.DependsOn);
    }
    [Fact] public void RetargetAlarm_SeparatesUncertainObservedReferencesFromCurrentTrigger()
    {
        var evidence = Evidence(); evidence = evidence with
        {
            Tags = evidence.Tags.Append(new HmiTagRowInfo { Name = "New", Table = "Other", Connection = "<Internal tag>" }).ToArray(),
            References = new[] { new HmiReference("T", "Alarm", "HmiDiscreteAlarm", "/Alarm"), new HmiReference("T", "Alarm", "HmiScreen", "/Screen") }
        };
        var state = HmiWorkingState.Create(evidence); var update = Item("update_alarm", "retarget"); update.AlarmName = "Alarm"; update.AlarmKind = "discrete"; update.TriggerTag = "New"; state.Apply(update);
        var delete = Item("delete_tag"); delete.TagName = "T";
        var oldResult = state.Resolve(delete);
        Assert.Contains(oldResult.Guards, g => g.Id == "hmi_state_unverifiable");
        Assert.Equal("HmiScreen", Assert.Single(oldResult.Effect!.Removes!.References).TypeName);
        Assert.Equal("retarget", oldResult.Effect.DependsOn);
        delete.TagName = "New"; var newResult = state.Resolve(delete);
        Assert.DoesNotContain(newResult.Guards, g => g.Id == "hmi_state_unverifiable");
        Assert.Equal("HmiDiscreteAlarm", Assert.Single(newResult.Effect!.Removes!.References).TypeName);
        Assert.Equal("retarget", newResult.Effect.DependsOn);
        Assert.Equal("T.Member", evidence.Alarms[0].TriggerTag);
    }
    [Fact] public void RetargetKnownInCallAlarm_RemovesOldTriggerWithoutBlocking()
    {
        var evidence = Evidence(); evidence = evidence with { Tags = evidence.Tags.Append(new HmiTagRowInfo { Name = "New", Table = "Other" }).ToArray(), Alarms = Array.Empty<HmiAlarmRowInfo>(), References = Array.Empty<HmiReference>() };
        var state = HmiWorkingState.Create(evidence); var alarm = Item("create_alarm", "created"); alarm.AlarmName = "NewAlarm"; alarm.AlarmKind = "analog"; alarm.TriggerTag = "T"; state.Apply(alarm);
        alarm.Operation = "update_alarm"; alarm.OperationId = "retarget"; alarm.TriggerTag = "New"; state.Apply(alarm);
        var delete = Item("delete_tag"); delete.TagName = "T"; var result = state.Resolve(delete);
        Assert.Empty(result.Effect!.Removes!.References); Assert.DoesNotContain(result.Guards, g => g.Id == "hmi_state_unverifiable");
        Assert.Equal("retarget", result.Effect.DependsOn);
    }
    [Theory] [InlineData("update_tag")] [InlineData("delete_tag")]
    public void ChangedReferencedTag_TouchesAlarmEvenWhenItsTriggerIsElsewhere(string operation)
    {
        var evidence = Evidence() with { References = new[] { new HmiReference("T", "Alarm", "HmiDiscreteAlarm", null) } };
        evidence.Alarms[0].TriggerTag = "Elsewhere";
        var state = HmiWorkingState.Create(evidence); var tag = Item(operation, "changed"); tag.TagName = "T"; if (operation == "update_tag") tag.NewName = "Renamed"; state.Apply(tag);
        var alarm = Item("update_alarm"); alarm.AlarmName = "Alarm"; alarm.AlarmKind = "discrete"; alarm.Priority = 7;
        Assert.Equal("changed", state.Resolve(alarm).Effect!.DependsOn);
    }
    [Fact] public void UncertainAlarmReference_FollowsRenameAndDisappearsWithAlarmDeletion()
    {
        var evidence = Evidence(); evidence = evidence with
        {
            Tags = evidence.Tags.Append(new HmiTagRowInfo { Name = "New", Table = "Other" }).ToArray(),
            References = new[] { new HmiReference("T", "Alarm", "HmiDiscreteAlarm", null) }
        };
        var state = HmiWorkingState.Create(evidence); var alarm = Item("update_alarm", "retarget"); alarm.AlarmName = "Alarm"; alarm.AlarmKind = "discrete"; alarm.TriggerTag = "New"; state.Apply(alarm);
        alarm.TriggerTag = null; alarm.NewName = "Renamed"; alarm.OperationId = "rename"; state.Apply(alarm);
        alarm.Operation = "delete_alarm"; alarm.AlarmName = "Renamed"; alarm.NewName = null; alarm.OperationId = "remove"; state.Apply(alarm);
        var delete = Item("delete_tag"); delete.TagName = "T"; var result = state.Resolve(delete);
        Assert.DoesNotContain(result.Guards, g => g.Id == "hmi_state_unverifiable");
        Assert.Empty(result.Effect!.Removes!.References); Assert.Equal("remove", result.Effect.DependsOn);
    }

    [Theory]
    [InlineData("create_tag_table")] [InlineData("update_tag_table")] [InlineData("create_tag")] [InlineData("update_tag")]
    [InlineData("create_alarm")] [InlineData("update_alarm")]
    public void NameInUse_ResolvesWithoutGuard(string operation)
    {
        var evidence = Evidence() with
        {
            Tags = new[] { new HmiTagRowInfo { Name = "T", Table = "Table", Connection = "<Internal tag>" }, new HmiTagRowInfo { Name = "U", Table = "Table", Connection = "<Internal tag>" } },
            Alarms = new[] { new HmiAlarmRowInfo { Name = "Alarm", Kind = "discrete" }, new HmiAlarmRowInfo { Name = "Other", Kind = "discrete" } }
        };
        var item = Item(operation); item.AlarmKind = "discrete"; item.TriggerTag = "T";
        switch (operation)
        {
            case "create_tag_table": item.TableName = "other"; break;
            case "update_tag_table": item.TableName = "Table"; item.NewName = "OTHER"; break;
            case "create_tag": item.TableName = "Other"; item.TagName = "t"; item.DataType = "Bool"; break;
            case "update_tag": item.TagName = "T"; item.NewName = "u"; break;
            case "create_alarm": item.AlarmName = "alarm"; break;
            default: item.AlarmName = "Alarm"; item.NewName = "OTHER"; break;
        }
        var result = HmiWorkingState.Create(evidence).Resolve(item);
        Assert.Null(result.Error); Assert.Empty(result.Guards);
    }
    [Theory]
    [InlineData("connection")] [InlineData("alarmClass")] [InlineData("triggerTag")] [InlineData("triggerMember")] [InlineData("group")] [InlineData("culture")]
    public void MissingReferences_ResolveWithoutError(string field)
    {
        var item = field is "connection" or "culture" ? Item("create_tag") : field == "group" ? Item("create_tag_table") : Item("create_alarm");
        item.TagName = "New"; item.TableName = "Table"; item.DataType = "Bool"; item.AlarmName = "New"; item.AlarmKind = "discrete"; item.TriggerTag = "T";
        if (field == "connection") { item.DataType = null; item.PlcTag = "DB.V"; item.Connection = "Missing"; }
        if (field == "alarmClass") item.AlarmClass = "Missing";
        if (field == "triggerTag") item.TriggerTag = "Missing";
        if (field == "triggerMember") item.TriggerTag = "T.Missing";
        if (field == "group") { item.TableName = "NewTable"; item.GroupPath = "Missing"; }
        if (field == "culture") item.Comment = new() { new("xx-XX", "text") };
        var result = HmiWorkingState.Create(Evidence()).Resolve(item);
        Assert.Null(result.Error); Assert.Empty(result.Guards);
    }
    [Fact] public void BoundTagDataTypeAndUnboundPlcTag_ResolveWithoutError()
    {
        var evidence = Evidence(); evidence.Tags[0].Connection = "PLC";
        var bound = Item("update_tag"); bound.TagName = "T"; bound.DataType = "Int";
        Assert.Null(HmiWorkingState.Create(evidence).Resolve(bound).Error);
        var unbound = Item("update_tag"); unbound.TagName = "T"; unbound.PlcTag = "DB.Value";
        var result = HmiWorkingState.Create(Evidence()).Resolve(unbound);
        Assert.Null(result.Error); Assert.Empty(result.Guards);
    }
    [Fact] public void UnreadableConnection_DoesNotBlockUpdate()
    {
        var evidence = Evidence(); evidence.Tags[0].Connection = null;
        var item = Item("update_tag"); item.TagName = "T"; item.DataType = "Int";
        var result = HmiWorkingState.Create(evidence).Resolve(item);
        Assert.Null(result.Error); Assert.Empty(result.Guards);
    }
    [Fact] public void CreateIntoUnknownTable_ResolvesWithRequestedTable()
    {
        var state = HmiWorkingState.Create(Evidence());
        var create = Item("create_tag", "create"); create.TableName = "Unknown"; create.TagName = "New"; create.DataType = "Bool";
        var effect = state.Resolve(create).Effect!;
        Assert.Equal("Unknown", effect.Target.TableName); Assert.Null(effect.Target.GroupPath);
        state.Apply(create);
        var update = Item("update_tag"); update.TagName = "New"; update.Comment = new() { new("en-US", "x") };
        var updated = state.Resolve(update);
        Assert.Null(updated.Error); Assert.Equal("Unknown", updated.Effect!.Target.TableName);
        var other = Item("update_tag"); other.TagName = "T"; other.Comment = new() { new("en-US", "x") };
        var existing = HmiWorkingState.Create(Evidence() with { Tables = new[] { new HmiTableEvidence("Other", null) } }).Resolve(other);
        Assert.Null(existing.Error); Assert.Equal("Table", existing.Effect!.Target.TableName); Assert.Null(existing.Effect.Target.GroupPath);
    }
    [Fact] public void DuplicateCreateThenUpdate_ResolvesToOneRow()
    {
        var state = HmiWorkingState.Create(Evidence());
        var table = Item("create_tag_table"); table.TableName = "table"; state.Apply(table);
        var tag = Item("create_tag"); tag.TableName = "Table"; tag.TagName = "t"; tag.DataType = "Bool"; state.Apply(tag);
        var alarm = Item("create_alarm"); alarm.AlarmName = "alarm"; alarm.AlarmKind = "discrete"; alarm.TriggerTag = "T"; state.Apply(alarm);
        var updateTag = Item("update_tag"); updateTag.TagName = "T"; updateTag.Comment = new() { new("en-US", "x") };
        var updateAlarm = Item("update_alarm"); updateAlarm.AlarmName = "Alarm"; updateAlarm.AlarmKind = "discrete"; updateAlarm.Priority = 1;
        var updateTable = Item("update_tag_table"); updateTable.TableName = "Table"; updateTable.NewName = "Renamed";
        Assert.Null(state.Resolve(updateTag).Error); Assert.Null(state.Resolve(updateAlarm).Error); Assert.Null(state.Resolve(updateTable).Error);
    }
    [Theory] [InlineData("tag")] [InlineData("tag_table")] [InlineData("alarm")]
    public void RenameIntoTakenName_KeepsBothObjectsAndCaseOnlyRenameStillApplies(string kind)
    {
        var evidence = Evidence() with
        {
            Tags = new[] { new HmiTagRowInfo { Name = "T", Table = "Table" }, new HmiTagRowInfo { Name = "U", Table = "Other" } },
            Alarms = new[] { new HmiAlarmRowInfo { Name = "Alarm", Kind = "discrete" }, new HmiAlarmRowInfo { Name = "Taken", Kind = "discrete" } }
        };
        var (first, taken) = kind switch { "tag" => ("T", "u"), "tag_table" => ("Table", "other"), _ => ("Alarm", "taken") };
        HmiWriteOperationRequest Update(string id, string name, string? newName)
        {
            var item = Item("update_" + kind, id); item.NewName = newName; item.AlarmKind = "discrete"; item.Priority = 1; item.Comment = new() { new("en-US", "x") };
            if (kind == "tag") item.TagName = name; else if (kind == "tag_table") item.TableName = name; else item.AlarmName = name;
            return item;
        }
        var state = HmiWorkingState.Create(evidence);
        state.Apply(Update("rename", first, taken));
        var later = state.Resolve(Update("later", taken, null));
        Assert.Null(later.Error); Assert.Empty(later.Guards);
        Assert.Equal(taken, later.Effect!.Target.Name, StringComparer.OrdinalIgnoreCase);
        Assert.Null(later.Effect.DependsOn); // the skipped rename never touched the name's holder
        Assert.Null(state.Resolve(Update("original", first, null)).Error);

        state = HmiWorkingState.Create(evidence);
        state.Apply(Update("case", first, first.ToUpperInvariant()));
        var renamed = state.Resolve(Update("after", first.ToUpperInvariant(), null));
        Assert.Null(renamed.Error); Assert.Equal(first.ToUpperInvariant(), renamed.Effect!.Target.Name);
        Assert.Equal("case", renamed.Effect.DependsOn);
    }
}
