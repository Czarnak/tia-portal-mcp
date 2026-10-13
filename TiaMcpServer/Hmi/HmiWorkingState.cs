using System.Globalization;
using System.Text.Json;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Hmi.HmiGuardDefinitions;

namespace TiaMcpServer.Hmi;

/// <summary>An owned working copy of read evidence, with earlier planned items applied in caller order.</summary>
internal sealed class HmiWorkingState
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    private readonly HmiEvidence _evidence;
    private readonly List<HmiTableEvidence> _tables;
    private readonly List<HmiTagRowInfo> _tags;
    private readonly List<HmiAlarmRowInfo> _alarms;
    private readonly List<string> _members;
    private readonly List<HmiReference> _references;
    private readonly List<HmiReference> _uncertainReferences = new();
    private readonly Dictionary<(string Kind, string Name, string? AlarmKind), (int Sequence, string Id)> _touches = new();
    private int _sequence;
    private sealed class ResolutionException(string category, string message) : Exception(message)
    { public string Category { get; } = category; }

    private HmiWorkingState(HmiEvidence evidence)
    {
        // Read DTOs are mutable; planned updates must not mutate the planner's original observations.
        _evidence = JsonSerializer.Deserialize<HmiEvidence>(JsonSerializer.Serialize(evidence))!;
        _tables = _evidence.Tables.ToList(); _tags = _evidence.Tags.ToList(); _alarms = _evidence.Alarms.ToList();
        _members = _evidence.MemberPaths.ToList(); _references = _evidence.References.ToList();
    }
    public static HmiWorkingState Create(HmiEvidence evidence) => new(evidence);

    // Only the planner may seed a newly created bound tag; dependent replans require observed members.
    internal void AddProvisionalMemberPath(string path)
    {
        if (!_members.Contains(path, Names)) _members.Add(path);
    }

    public HmiResolution Resolve(HmiWriteOperationRequest item)
    {
        var guards = new List<FiredGuard>();
        if (!_evidence.Complete) guards.Add(Fire(StateUnverifiable, item, "Required HMI read evidence is incomplete."));
        try { return new(ResolveEffect(item, guards), guards, null); }
        catch (ResolutionException ex)
        {
            if (!_evidence.Complete && ex.Category == "target_not_found")
                return new(new(item.Operation, RequestedIdentity(item), Array.Empty<HmiFieldChange>(), null, null), guards, null);
            return new(null, guards, new WriteToolError(ex.Category, ex.Message));
        }
    }

    public string? LastTouchedBy(HmiTargetIdentity target) => _touches.TryGetValue(Key(target), out var touch) ? touch.Id : null;
    private static (string, string, string?) Key(HmiTargetIdentity target) => (target.Kind, target.Name.ToUpperInvariant(), target.AlarmKind);
    private void Touch(HmiTargetIdentity target, string id) => _touches[Key(target)] = (++_sequence, id);
    private HmiTargetIdentity Identity(string kind, string name, string? alarmKind = null, string? table = null, string? group = null)
        => new(kind, _evidence.HmiName, name, alarmKind, table, group);
    private HmiTargetIdentity RequestedIdentity(HmiWriteOperationRequest r) => r.Operation.Contains("tag_table", StringComparison.Ordinal)
        ? Identity("TagTable", r.TableName!, table: r.TableName, group: r.GroupPath)
        : r.Operation.EndsWith("alarm", StringComparison.Ordinal) ? Identity("Alarm", r.AlarmName!, r.AlarmKind)
        : Identity("Tag", r.TagName!, table: r.TableName);
    private static FiredGuard Fire(string id, HmiWriteOperationRequest r, string message) => new(id, r.OperationId, message);
    private static T Require<T>(IEnumerable<T> source, Func<T, bool> predicate, string description)
    {
        var matches = source.Where(predicate).Take(2).ToArray();
        if (matches.Length != 1) throw new ResolutionException(matches.Length == 0 ? "target_not_found" : "target_ambiguous", $"Cannot uniquely resolve {description}.");
        return matches[0];
    }
    private HmiTableEvidence Table(string name) => Require(_tables, t => Names.Equals(t.Name, name), $"table '{name}'");
    private HmiTagRowInfo Tag(string name) => Require(_tags, t => Names.Equals(t.Name, name), $"tag '{name}'");
    private HmiAlarmRowInfo Alarm(string name, string? kind) => Require(_alarms, a => a.Kind == kind && Names.Equals(a.Name, name), $"{kind} alarm '{name}'");
    private static bool RefersTo(string? reference, string tag) => reference is not null && (Names.Equals(reference, tag) || reference.StartsWith(tag + ".", StringComparison.OrdinalIgnoreCase));
    private static string RenameReference(string reference, string oldName, string newName) => RefersTo(reference, oldName) ? newName + reference[oldName.Length..] : reference;
    private string? Dependency(IEnumerable<HmiTargetIdentity> targets) => targets.Select(t => _touches.GetValueOrDefault(Key(t)))
        .OrderByDescending(t => t.Sequence).Select(t => t.Id).FirstOrDefault();

    private HmiWriteEffect ResolveEffect(HmiWriteOperationRequest r, List<FiredGuard> guards)
    {
        var create = r.Operation.StartsWith("create_", StringComparison.Ordinal);
        var delete = r.Operation.StartsWith("delete_", StringComparison.Ordinal);
        var target = RequestedIdentity(r);
        var dependencies = new List<HmiTargetIdentity>();
        var changes = new List<HmiFieldChange>();
        HmiTagRowInfo? tag = null; HmiAlarmRowInfo? alarm = null;
        void Change(string field, object? current, object? requested, bool derived = false)
        {
            if (requested is not null || derived) changes.Add(new(field, Text(current), Text(requested), derived));
        }
        void TextChanges(string field, List<HmiText>? current, List<HmiText>? requested)
        {
            foreach (var text in requested ?? new())
                changes.Add(new($"{field}[{text.Culture}]", current?.FirstOrDefault(t => Names.Equals(t.Culture, text.Culture))?.Text, text.Text, false));
        }
        void Reference(IEnumerable<string> names, string name, string kind) => Require(names, n => Names.Equals(n, name), $"{kind} '{name}'");
        void Collision(bool collides)
        {
            if (collides) guards.Add(Fire(NameCollision, r, $"The requested name '{r.NewName ?? target.Name}' already exists in its HMI scope."));
        }
        if (target.Kind == "TagTable")
        {
            var table = create ? null : Table(r.TableName!);
            target = Identity("TagTable", table?.Name ?? r.TableName!, table: table?.Name ?? r.TableName, group: table?.GroupPath ?? r.GroupPath);
            if (create && r.GroupPath is not null) Reference(_evidence.Groups, r.GroupPath, "group");
            if (create || r.NewName is not null) Collision(_tables.Any(t => !ReferenceEquals(t, table) && Names.Equals(t.Name, r.NewName ?? r.TableName)));
        }
        else if (target.Kind == "Tag")
        {
            tag = create ? null : Tag(r.TagName!);
            var table = create ? Table(r.TableName!) : tag!.Table is { } tableName ? Table(tableName) : null;
            target = Identity("Tag", tag?.Name ?? r.TagName!, table: table?.Name, group: table?.GroupPath);
            if (table is not null) dependencies.Add(Identity("TagTable", table.Name));
            if (create || r.NewName is not null) Collision(_tags.Any(t => !ReferenceEquals(t, tag) && Names.Equals(t.Name, r.NewName ?? r.TagName)));
            if (r.Connection is not null) Reference(_evidence.Connections, r.Connection, "connection");
            var connection = r.Connection ?? tag?.Connection;
            var bound = connection is not null && !Names.Equals(connection, "<Internal tag>") && connection.Length > 0;
            if (!create && (r.PlcTag is not null || r.DataType is not null) && connection is null)
                guards.Add(Fire(StateUnverifiable, r, "The tag connection is unreadable."));
            else if (r.PlcTag is not null && !bound)
                throw new ResolutionException("validation_error", "plcTag requires a bound tag or an explicit connection.");
            if (r.DataType is not null && bound)
                throw new ResolutionException("validation_error", "dataType can only be written for an internal tag.");
            Change("connection", tag?.Connection, r.Connection); Change("plcTag", tag?.PlcTag, r.PlcTag);
            Change("dataType", tag?.DataType, r.DataType, !delete && bound); Change("plcName", tag?.PlcName, null, !delete && bound);
            Change("acquisitionCycle", tag?.AcquisitionCycle, r.AcquisitionCycle);
            TextChanges("comment", tag?.Comment, r.Comment); TextChanges("displayName", tag?.DisplayName, r.DisplayName);
        }
        else
        {
            alarm = create ? null : Alarm(r.AlarmName!, r.AlarmKind);
            target = Identity("Alarm", alarm?.Name ?? r.AlarmName!, r.AlarmKind);
            if (create || r.NewName is not null) Collision(_alarms.Any(a => !ReferenceEquals(a, alarm) && a.Kind == r.AlarmKind && Names.Equals(a.Name, r.NewName ?? r.AlarmName)));
            if (r.AlarmClass is not null) Reference(_evidence.AlarmClasses, r.AlarmClass, "alarm class");
            if (r.TriggerTag is not null)
            {
                var parent = r.TriggerTag.Split('.')[0]; var trigger = Tag(parent);
                if (!Names.Equals(r.TriggerTag, parent)) Reference(_members, r.TriggerTag, "tag member");
                dependencies.Add(Identity("Tag", trigger.Name));
            }
            Change("triggerTag", alarm?.TriggerTag, r.TriggerTag); Change("bitNumber", alarm?.TriggerBit, r.BitNumber);
            Change("triggerMode", alarm?.TriggerMode, r.TriggerMode); Change("condition", alarm?.Condition, r.Condition);
            Change("conditionValue", alarm?.ConditionValue?.Value, r.ConditionValue);
            Change("alarmClass", alarm?.AlarmClass, r.AlarmClass); Change("priority", alarm?.Priority, r.Priority);
            TextChanges("eventText", alarm?.EventText, HmiWriteCatalog.ToWorkerRequest(r, _evidence.HmiName).EventText); TextChanges("infoText", alarm?.InfoText, r.InfoText);
            if (!delete) Change(r.AlarmKind == "discrete" ? "triggerBitAddress" : "triggerAddress", r.AlarmKind == "discrete" ? alarm?.TriggerBitAddress : alarm?.TriggerAddress, null, true);
        }
        foreach (var text in new[] { r.Comment, r.DisplayName, r.EventText, r.InfoText }.Where(t => t is not null).SelectMany(t => t!))
            Reference(_evidence.Cultures, text.Culture, "project culture");
        Change("name", create ? null : target.Name, r.NewName ?? (create ? target.Name : null));
        dependencies.Add(target);
        if (r.NewName is not null) dependencies.Add(target with { Name = r.NewName });
        HmiRemoval? removes = null;
        if (delete)
        {
            var removedTags = target.Kind == "TagTable" ? _tags.Where(t => Names.Equals(t.Table, target.Name)).Select(t => t.Name).ToArray()
                : target.Kind == "Tag" ? new[] { target.Name } : Array.Empty<string>();
            dependencies.AddRange(removedTags.Select(t => Identity("Tag", t)));
            if (_uncertainReferences.Any(reference => removedTags.Any(t => RefersTo(reference.Tag, t))))
                guards.Add(Fire(StateUnverifiable, r, "An alarm trigger changed; observed references do not identify which alarm property used this tag. Fresh reference evidence is required."));
            var references = _references.Where(reference => removedTags.Any(t => RefersTo(reference.Tag, t))).ToList();
            foreach (var removed in removedTags)
                references.AddRange(_alarms.Where(a => RefersTo(a.TriggerTag, removed)).Select(a => new HmiReference(removed, a.Name, a.Kind == "discrete" ? "HmiDiscreteAlarm" : "HmiAnalogAlarm", null)));
            dependencies.AddRange(references.Where(reference => reference.TypeName is "HmiDiscreteAlarm" or "HmiAnalogAlarm")
                .Select(reference => Identity("Alarm", reference.Name, reference.TypeName == "HmiDiscreteAlarm" ? "discrete" : "analog")));
            removes = new(removedTags, references.Distinct().ToArray());
            if (target.Kind == "TagTable") guards.Add(Fire(DeletesTableContents, r, $"Deleting table '{target.Name}' removes {removedTags.Length} tags."));
            if (references.Count > 0) guards.Add(Fire(DeletesReferencedTag, r, $"Deleted tags have {removes.References.Count} references; these are left dangling."));
        }
        return new(r.Operation, target, changes, removes, Dependency(dependencies));
    }

    private static string? Text(object? value) => value switch
    {
        null => null, string s => s, JsonElement scalar => scalar.ToString(), IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => throw new ArgumentException("A field change must contain scalar values.", nameof(value))
    };

    public void Apply(HmiWriteOperationRequest r)
    {
        var resolved = Resolve(r);
        if (resolved.Error is not null || resolved.Guards.Any(g => g.Id is NameCollision or StateUnverifiable)) return;
        var target = resolved.Effect!.Target;
        switch (r.Operation)
        {
            case "create_tag_table": _tables.Add(new(r.TableName!, r.GroupPath)); break;
            case "update_tag_table":
                var table = Table(r.TableName!); _tables[_tables.IndexOf(table)] = table with { Name = r.NewName! };
                foreach (var member in _tags.Where(t => Names.Equals(t.Table, table.Name)))
                { member.Table = r.NewName; Touch(Identity("Tag", member.Name), r.OperationId); }
                break;
            case "delete_tag_table":
                _tables.Remove(Table(r.TableName!));
                foreach (var removed in resolved.Effect.Removes!.Tags) RemoveTag(removed, r.OperationId);
                break;
            case "create_tag":
                var created = new HmiTagRowInfo { Name = r.TagName!, Table = Table(r.TableName!).Name, Connection = "<Internal tag>" };
                ApplyTag(created, r); _tags.Add(created); break;
            case "update_tag":
                var tag = Tag(r.TagName!); ApplyTag(tag, r);
                if (r.NewName is not null)
                {
                    var old = tag.Name; TouchReferencingAlarms(old, r.OperationId); tag.Name = r.NewName;
                    for (var i = 0; i < _members.Count; i++) _members[i] = RenameReference(_members[i], old, r.NewName);
                    foreach (var references in new[] { _references, _uncertainReferences })
                        for (var i = 0; i < references.Count; i++) references[i] = references[i] with { Tag = RenameReference(references[i].Tag, old, r.NewName) };
                    foreach (var a in _alarms.Where(a => RefersTo(a.TriggerTag, old)))
                        a.TriggerTag = RenameReference(a.TriggerTag!, old, r.NewName);
                }
                break;
            case "delete_tag": RemoveTag(target.Name, r.OperationId); break;
            case "create_alarm":
                var createdAlarm = new HmiAlarmRowInfo { Name = r.AlarmName!, Kind = r.AlarmKind! }; ApplyAlarm(createdAlarm, r); _alarms.Add(createdAlarm); TouchAlarmReferences(createdAlarm, r.OperationId); break;
            case "update_alarm":
                var alarm = Alarm(r.AlarmName!, r.AlarmKind);
                TouchAlarmReferences(alarm, r.OperationId);
                if (r.TriggerTag is not null && !Names.Equals(r.TriggerTag, alarm.TriggerTag))
                {
                    // UsedBy does not distinguish trigger, acknowledgement and parameter references.
                    var uncertain = _references.Where(reference => IsAlarmReference(reference, alarm)
                        && RefersTo(alarm.TriggerTag, reference.Tag)).ToArray();
                    _uncertainReferences.AddRange(uncertain);
                    foreach (var reference in uncertain) _references.Remove(reference);
                }
                ApplyAlarm(alarm, r);
                if (r.NewName is not null)
                    foreach (var references in new[] { _references, _uncertainReferences })
                        for (var i = 0; i < references.Count; i++)
                            if (IsAlarmReference(references[i], alarm)) references[i] = references[i] with { Name = r.NewName };
                alarm.Name = r.NewName ?? alarm.Name; TouchAlarmReferences(alarm, r.OperationId); break;
            case "delete_alarm":
                var removedAlarm = Alarm(r.AlarmName!, r.AlarmKind);
                TouchAlarmReferences(removedAlarm, r.OperationId);
                _references.RemoveAll(reference => IsAlarmReference(reference, removedAlarm));
                _uncertainReferences.RemoveAll(reference => IsAlarmReference(reference, removedAlarm)); _alarms.Remove(removedAlarm); break;
        }
        if (target.Kind == "Tag" && target.TableName is not null) Touch(Identity("TagTable", target.TableName), r.OperationId);
        Touch(target, r.OperationId);
        if (r.NewName is not null) Touch(target with { Name = r.NewName }, r.OperationId);
    }
    private void TouchAlarmReferences(HmiAlarmRowInfo alarm, string id)
    {
        if (alarm.TriggerTag is { } trigger) Touch(Identity("Tag", trigger.Split('.')[0]), id);
        foreach (var reference in _references.Concat(_uncertainReferences).Where(reference => IsAlarmReference(reference, alarm)))
            Touch(Identity("Tag", reference.Tag.Split('.')[0]), id);
    }
    private static bool IsAlarmReference(HmiReference reference, HmiAlarmRowInfo alarm) => Names.Equals(reference.Name, alarm.Name)
        && reference.TypeName == (alarm.Kind == "discrete" ? "HmiDiscreteAlarm" : "HmiAnalogAlarm");
    private void TouchReferencingAlarms(string tag, string id)
    {
        foreach (var alarm in _alarms.Where(a => RefersTo(a.TriggerTag, tag)
            || _references.Concat(_uncertainReferences).Any(reference => RefersTo(reference.Tag, tag) && IsAlarmReference(reference, a))))
            Touch(Identity("Alarm", alarm.Name, alarm.Kind), id);
    }
    private void RemoveTag(string name, string id)
    {
        TouchReferencingAlarms(name, id);
        _tags.RemoveAll(t => Names.Equals(t.Name, name)); _members.RemoveAll(m => RefersTo(m, name));
        _references.RemoveAll(r => RefersTo(r.Tag, name)); Touch(Identity("Tag", name), id);
    }
    private static List<HmiText>? MergeTexts(List<HmiText>? current, List<HmiText>? requested)
    {
        if (requested is null) return current;
        return (current ?? new()).Where(t => !requested.Any(r => Names.Equals(r.Culture, t.Culture))).Concat(requested).ToList();
    }
    private static void ApplyTag(HmiTagRowInfo tag, HmiWriteOperationRequest r)
    {
        tag.Connection = r.Connection ?? tag.Connection; tag.PlcTag = r.PlcTag ?? tag.PlcTag;
        tag.DataType = r.DataType ?? tag.DataType; tag.AcquisitionCycle = r.AcquisitionCycle ?? tag.AcquisitionCycle;
        tag.Comment = MergeTexts(tag.Comment, r.Comment); tag.DisplayName = MergeTexts(tag.DisplayName, r.DisplayName);
    }
    private static void ApplyAlarm(HmiAlarmRowInfo alarm, HmiWriteOperationRequest r)
    {
        alarm.TriggerTag = r.TriggerTag ?? alarm.TriggerTag; alarm.TriggerBit = r.BitNumber ?? alarm.TriggerBit;
        alarm.TriggerMode = r.TriggerMode ?? alarm.TriggerMode; alarm.Condition = r.Condition ?? alarm.Condition;
        var normalized = HmiWriteCatalog.ToWorkerRequest(r, r.HmiName ?? string.Empty);
        if (normalized.ConditionValueInt32 is { } integer) alarm.ConditionValue = new("System.Int32", JsonSerializer.SerializeToElement(integer));
        else if (normalized.ConditionValueDouble is { } number) alarm.ConditionValue = new("System.Double", JsonSerializer.SerializeToElement(number));
        alarm.AlarmClass = r.AlarmClass ?? alarm.AlarmClass; alarm.Priority = r.Priority ?? alarm.Priority;
        alarm.EventText = MergeTexts(alarm.EventText, normalized.EventText); alarm.InfoText = MergeTexts(alarm.InfoText, r.InfoText);
    }
}
