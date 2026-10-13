using System.Text.Json;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.CrossReferences;
using TiaMcpServer.Json;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Hmi;

/// <summary>Plans from complete public reads; retains original tag identities across caller-order edits.</summary>
public sealed class HmiWritePlanner(OpennessWorkerClient client)
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    private readonly Dictionary<string, string> _resolved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _deleteTags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _createdDeleteTags = new(StringComparer.Ordinal);
    private sealed class ReadFailure(string category, string message) : Exception(message)
    { public string Category { get; } = category; }
    private sealed record Origin(string? Name, bool Bound);
    private sealed class Context(HmiEvidence evidence)
    {
        public HmiEvidence Evidence = evidence;
        public readonly List<(HmiWriteOperationRequest Item, string? Provisional)> History = new();
        public readonly Dictionary<string, Origin> Origins = evidence.Tags.GroupBy(t => t.Name, Names)
            .ToDictionary(g => g.Key, g => new Origin(g.Key, Bound(g.First().Connection)), Names);
        public readonly HashSet<string> MemberReads = new(Names);
        public readonly HashSet<string> ReferenceReads = new(Names);
        public HmiWorkingState Replay()
        {
            var state = HmiWorkingState.Create(Evidence);
            foreach (var (item, provisional) in History)
            {
                if (provisional is not null) state.AddProvisionalMemberPath(provisional);
                state.Apply(item);
            }
            return state;
        }
    }
    public string ResolvedHmiName(string operationId) => _resolved[operationId];

    public async Task<HmiPlanResult> PlanAsync(string? projectPath, IReadOnlyList<HmiWriteOperationRequest> items)
    {
        _resolved.Clear(); _deleteTags.Clear(); _createdDeleteTags.Clear();
        var guards = new Dictionary<string, IReadOnlyList<FiredGuard>>(StringComparer.Ordinal);
        try
        {
            var devices = await Read<HmiDeviceListInfo>(projectPath, "list_hmi_devices", new());
            var contexts = new Dictionary<string, Context>(Names);
            var plans = new List<ItemPlan<HmiWriteEffect>>();
            var queryCount = 0;
            foreach (var item in items)
            {
                var hmi = ResolveHmi(devices, item.HmiName);
                _resolved[item.OperationId] = hmi;
                if (!contexts.TryGetValue(hmi, out var context))
                    contexts[hmi] = context = new(await Load(projectPath, hmi, devices));

                string? provisional = null;
                if (item.TriggerTag is { } trigger && trigger.Contains('.'))
                {
                    var parent = trigger.Split('.')[0];
                    if (context.Origins.TryGetValue(parent, out var origin))
                    {
                        if (origin.Name is null && origin.Bound) provisional = trigger;
                        else if (origin.Name is { } original && context.MemberReads.Add(original))
                        {
                            var detail = await Read<HmiTagDetailInfo>(projectPath, "get_tag", new() { HmiName = hmi, TagName = original });
                            context.Evidence = context.Evidence with
                            {
                                Complete = context.Evidence.Complete && detail.IsComplete && !detail.MembersTruncated,
                                MemberPaths = context.Evidence.MemberPaths.Concat(MemberPaths(detail.Members, original)).ToArray()
                            };
                        }
                    }
                }
                var state = context.Replay();
                if (provisional is not null) state.AddProvisionalMemberPath(provisional);
                var resolution = state.Resolve(item);
                if (resolution.Error is { } error) return Failed(error);

                if (resolution.Effect!.Removes is { } removal)
                {
                    var originals = removal.Tags.Where(context.Origins.ContainsKey).Select(t => context.Origins[t].Name)
                        .OfType<string>().Distinct(Names).Where(t => !context.ReferenceReads.Contains(t)).ToArray();
                    _deleteTags[item.OperationId] = new(removal.Tags, Names);
                    _createdDeleteTags[item.OperationId] = new(removal.Tags.Where(t => context.Origins.TryGetValue(t, out var origin) && origin.Name is null), Names);
                    if (queryCount + originals.Length > 200) context.Evidence = context.Evidence with { Complete = false };
                    else
                    {
                        foreach (var original in originals)
                        {
                            queryCount++;
                            context.ReferenceReads.Add(original);
                            var (references, complete) = await ReadReferences(projectPath, hmi, original);
                            context.Evidence = context.Evidence with { References = context.Evidence.References.Concat(references).ToArray(), Complete = context.Evidence.Complete && complete };
                        }
                    }
                    state = context.Replay();
                    if (provisional is not null) state.AddProvisionalMemberPath(provisional);
                    resolution = state.Resolve(item);
                    if (resolution.Error is { } deleteError) return Failed(deleteError);
                }
                var effect = resolution.Effect!;
                plans.Add(ItemPlan<HmiWriteEffect>.Resolved(effect) with { DependsOn = effect.DependsOn });
                guards[item.OperationId] = resolution.Guards;
                if (!resolution.Guards.Any(g => g.Id is HmiGuardDefinitions.NameCollision or HmiGuardDefinitions.StateUnverifiable))
                {
                    context.History.Add((item, provisional));
                    TrackOrigins(context, item, effect);
                }
            }
            // Later deletes can reveal references affected by earlier items. Resolve final effects only
            // after all that evidence is present, so their dependencies use the same complete observation.
            plans.Clear(); guards.Clear();
            var finalStates = contexts.ToDictionary(pair => pair.Key, pair => HmiWorkingState.Create(pair.Value.Evidence), Names);
            var provisionalPaths = contexts.Values.SelectMany(c => c.History).Where(h => h.Provisional is not null)
                .ToDictionary(h => h.Item.OperationId, h => h.Provisional!, StringComparer.Ordinal);
            foreach (var item in items)
            {
                var state = finalStates[_resolved[item.OperationId]];
                if (provisionalPaths.TryGetValue(item.OperationId, out var member)) state.AddProvisionalMemberPath(member);
                var resolution = state.Resolve(item);
                if (resolution.Error is { } error) return Failed(error);
                plans.Add(ItemPlan<HmiWriteEffect>.Resolved(resolution.Effect!) with { DependsOn = resolution.Effect!.DependsOn });
                guards[item.OperationId] = resolution.Guards;
                state.Apply(item);
            }
            return new(WritePlan<HmiWriteEffect>.Ok(plans), guards);
        }
        catch (ReadFailure failure) { return Failed(new(failure.Category, failure.Message)); }
    }

    public async Task<HmiReplanResult> ReplanAsync(string? projectPath, HmiWriteOperationRequest item)
    {
        try
        {
            var hmi = ResolvedHmiName(item.OperationId);
            var evidence = await LoadItem(projectPath, hmi, item);
            var state = HmiWorkingState.Create(evidence);
            var resolution = state.Resolve(item);
            if (resolution.Error is { } error) return new(ItemReplan<HmiWriteEffect>.Fail(error.Category, error.Message), []);
            if (resolution.Effect!.Removes is { } removes)
            {
                var covered = _deleteTags.GetValueOrDefault(item.OperationId);
                var created = _createdDeleteTags.GetValueOrDefault(item.OperationId);
                // Re-reads consume no new budget: every original tag must already have been counted at planning.
                if (covered is null || removes.Tags.Any(t => !covered.Contains(t))) evidence = evidence with { Complete = false };
                else foreach (var tag in removes.Tags.Where(t => created?.Contains(t) != true))
                {
                    var (references, complete) = await ReadReferences(projectPath, hmi, tag);
                    evidence = evidence with { References = evidence.References.Concat(references).ToArray(), Complete = evidence.Complete && complete };
                }
                resolution = HmiWorkingState.Create(evidence).Resolve(item);
            }
            return resolution.Error is { } lateError
                ? new(ItemReplan<HmiWriteEffect>.Fail(lateError.Category, lateError.Message), [])
                : new(ItemReplan<HmiWriteEffect>.Ok(ItemPlan<HmiWriteEffect>.Resolved(resolution.Effect!)), resolution.Guards);
        }
        catch (ReadFailure failure) { return new(ItemReplan<HmiWriteEffect>.Fail(failure.Category, failure.Message), []); }
    }

    private static void TrackOrigins(Context context, HmiWriteOperationRequest item, HmiWriteEffect effect)
    {
        if (effect.Removes is { } removal) foreach (var tag in removal.Tags) context.Origins.Remove(tag);
        if (item.Operation == "create_tag") context.Origins[item.TagName!] = new(null, Bound(item.Connection));
        if (item.Operation == "update_tag" && context.Origins.TryGetValue(effect.Target.Name, out var origin))
        {
            context.Origins.Remove(effect.Target.Name);
            context.Origins[item.NewName ?? effect.Target.Name] = origin with { Bound = item.Connection is null ? origin.Bound : Bound(item.Connection) };
        }
    }
    private static bool Bound(string? connection) => !string.IsNullOrEmpty(connection) && !Names.Equals(connection, "<Internal tag>");
    private static HmiPlanResult Failed(WriteToolError error) => new(WritePlan<HmiWriteEffect>.Fail(error.Category, error.Message), new Dictionary<string, IReadOnlyList<FiredGuard>>());
    private static string ResolveHmi(HmiDeviceListInfo devices, string? name)
    {
        var matches = devices.Devices.Where(d => name is null ? d.Kind == "unified" : Names.Equals(name, d.SoftwareName) || Names.Equals(name, d.DeviceName)).ToArray();
        if (matches.Length != 1) throw new ReadFailure(matches.Length == 0 ? "target_not_found" : "target_ambiguous", "The HMI selector must resolve exactly one Unified HMI.");
        if (matches[0].Kind != "unified") throw new ReadFailure("target_kind_unsupported", "HMI writes require Unified software.");
        return matches[0].SoftwareName;
    }

    private async Task<HmiEvidence> Load(string? path, string hmi, HmiDeviceListInfo devices)
    {
        var evidence = await LoadCommon(path, hmi, true, true, true);
        var (tags, tagsComplete) = await Tags(path, hmi, null);
        var (alarms, alarmsComplete) = await Alarms(path, hmi);
        return evidence with { Devices = devices.Devices, Tags = tags, Alarms = alarms, Complete = evidence.Complete && devices.IsComplete && tagsComplete && alarmsComplete };
    }
    private async Task<HmiEvidence> LoadCommon(string? path, string hmi, bool cultures, bool connections, bool classes)
    {
        var tables = await Read<HmiTagTableTreeInfo>(path, "list_tag_tables", new() { HmiName = hmi });
        var flattened = new List<HmiTableEvidence>(); var groups = new List<string>();
        void Flatten(IEnumerable<HmiTagTableInfo> ts, IEnumerable<HmiTagTableGroupInfo> gs, string? parent)
        {
            flattened.AddRange(ts.Select(t => new HmiTableEvidence(t.Name, parent)));
            foreach (var group in gs)
            {
                var child = parent is null ? group.Name : parent + "/" + group.Name;
                groups.Add(child); Flatten(group.Tables, group.Groups, child);
            }
        }
        Flatten(tables.Tables, tables.Groups, tables.GroupPath);
        var languages = cultures ? await Read<HmiProjectLanguagesInfo>(path, "list_project_languages", new() { HmiName = hmi }) : new();
        var cs = connections ? await Read<HmiConnectionListInfo>(path, "list_connections", new() { HmiName = hmi }) : new();
        var ac = classes ? await Read<HmiAlarmClassesInfo>(path, "list_alarm_classes", new() { HmiName = hmi }) : new();
        return new() { HmiName = hmi, Tables = flattened, Groups = groups, Cultures = languages.Languages, Connections = cs.Connections.Select(c => c.Name).ToArray(), AlarmClasses = ac.AlarmClasses.Select(c => c.Name).ToArray(), Complete = tables.IsComplete && languages.IsComplete && cs.IsComplete && ac.IsComplete };
    }

    private async Task<HmiEvidence> LoadItem(string? path, string hmi, HmiWriteOperationRequest item)
    {
        var evidence = await LoadCommon(path, hmi, new[] { item.Comment, item.DisplayName, item.EventText, item.InfoText }.Any(t => t is not null), item.Connection is not null, item.AlarmClass is not null);
        var tags = new List<HmiTagRowInfo>(); var alarms = new List<HmiAlarmRowInfo>(); var members = new List<string>();
        var complete = evidence.Complete;
        var tagNames = new Dictionary<string, bool>(Names);
        void NeedTag(string? name, bool required) { if (name is not null) tagNames[name] = required || tagNames.GetValueOrDefault(name); }
        if (item.Operation.EndsWith("_tag", StringComparison.Ordinal))
        {
            NeedTag(item.TagName, item.Operation != "create_tag"); NeedTag(item.NewName, false);
        }
        if (item.TriggerTag is { } trigger) NeedTag(trigger.Split('.')[0], true);
        foreach (var (name, required) in tagNames)
        {
            var detail = await Read<HmiTagDetailInfo>(path, "get_tag", new() { HmiName = hmi, TagName = name }, optional: !required);
            if (detail is null) continue;
            tags.Add(detail.Tag); members.AddRange(MemberPaths(detail.Members, name));
            complete &= detail.IsComplete && (!(item.TriggerTag?.Contains('.') == true && Names.Equals(item.TriggerTag.Split('.')[0], name)) || !detail.MembersTruncated);
        }
        if (item.Operation.EndsWith("_alarm", StringComparison.Ordinal))
        {
            foreach (var name in new[] { item.AlarmName, item.NewName }.OfType<string>().Distinct(Names))
            {
                var detail = await Read<HmiAlarmDetailInfo>(path, "get_alarm", new() { HmiName = hmi, AlarmName = name, AlarmKind = item.AlarmKind }, optional: item.Operation == "create_alarm" || !Names.Equals(name, item.AlarmName));
                if (detail is null) continue;
                alarms.Add(detail.Alarm); complete &= detail.IsComplete;
            }
        }
        if (item.Operation == "delete_tag_table")
        {
            var tableTags = await Tags(path, hmi, item.TableName); tags.AddRange(tableTags.Rows); complete &= tableTags.Complete;
        }
        if (item.Operation is "delete_tag" or "delete_tag_table")
        {
            var currentAlarms = await Alarms(path, hmi); alarms.AddRange(currentAlarms.Rows); complete &= currentAlarms.Complete;
        }
        return evidence with { Tags = tags, Alarms = alarms, MemberPaths = members, Complete = complete };
    }

    private Task<(List<HmiTagRowInfo> Rows, bool Complete)> Tags(string? path, string hmi, string? table)
        => Pages<HmiTagListInfo, HmiTagRowInfo>(path, "list_tags", new() { HmiName = hmi, TableName = table }, p => (p.Tags, p.Page, p.IsComplete));
    private Task<(List<HmiAlarmRowInfo> Rows, bool Complete)> Alarms(string? path, string hmi)
        => Pages<HmiAlarmListInfo, HmiAlarmRowInfo>(path, "list_alarms", new() { HmiName = hmi }, p => (p.Alarms, p.Page, p.IsComplete));
    private async Task<(List<TRow> Rows, bool Complete)> Pages<T, TRow>(string? path, string operation, HmiQueryInfo query, Func<T, (List<TRow> Rows, HmiPage Page, bool Complete)> unpack) where T : class
    {
        var rows = new List<TRow>(); var complete = true; int? total = null; string? snapshot = null;
        query.Offset = 0; query.Limit = 2000;
        while (true)
        {
            var (pageRows, page, pageComplete) = unpack(await Read<T>(path, operation, query));
            rows.AddRange(pageRows); complete &= pageComplete;
            if (page.Offset != query.Offset || page.Limit != 2000 || page.Total < rows.Count || total is not null && total != page.Total) return (rows, false);
            total = page.Total;
            if (page.NextOffset is null) return (rows, complete && rows.Count == page.Total && page.SnapshotId is null);
            if (pageRows.Count == 0 || page.NextOffset != rows.Count || page.NextOffset >= page.Total || string.IsNullOrEmpty(page.SnapshotId) || snapshot is not null && snapshot != page.SnapshotId) return (rows, false);
            snapshot = page.SnapshotId; query.SnapshotId = snapshot; query.Offset = page.NextOffset;
        }
    }

    private static IEnumerable<string> MemberPaths(IEnumerable<HmiTagMemberInfo> members, string parent)
    {
        foreach (var member in members)
        {
            // Public rows already carry full paths; never invent a path by prefixing an unrelated row.
            if (!member.Tag.Name.StartsWith(parent + ".", StringComparison.OrdinalIgnoreCase)) continue;
            yield return member.Tag.Name;
            foreach (var nested in MemberPaths(member.Members, member.Tag.Name)) yield return nested;
        }
    }
    private async Task<T> Read<T>(string? path, string operation, HmiQueryInfo query, bool optional = false) where T : class
    {
        var result = await client.ReadHmiAsync(HmiOperationCatalog.WorkerMethod(operation), query, path).ConfigureAwait(false);
        if (!result.Success)
        {
            if (optional && result.FailureCategory == "target_not_found") return null!;
            throw new ReadFailure(result.FailureCategory ?? "worker_operation_failed", result.Error ?? "Required HMI evidence could not be read.");
        }
        try
        {
            var value = CanonicalJson.DeserializeWorkerPayload<T>(result.Payload);
            if (value is null) throw new JsonException();
            ValidateRows(value);
            return value;
        }
        catch (JsonException) { throw new ReadFailure("protocol_error", "Required HMI evidence did not match its typed payload contract."); }
    }
    private static void ValidateRows(object value)
    {
        static void Rows<T>(IEnumerable<T> rows, Action<T> validate) where T : class
        {
            foreach (var row in rows)
            {
                if (row is null) throw new JsonException();
                validate(row);
            }
        }
        static void Name(string name) { if (string.IsNullOrWhiteSpace(name)) throw new JsonException(); }
        static void Tag(HmiTagRowInfo tag) { if (tag is null) throw new JsonException(); Name(tag.Name); }
        static void Alarm(HmiAlarmRowInfo alarm) { if (alarm is null) throw new JsonException(); Name(alarm.Name); if (alarm.Kind is not ("discrete" or "analog")) throw new JsonException(); }
        static void Groups(IEnumerable<HmiTagTableGroupInfo> groups) => Rows(groups, g => { Name(g.Name); Rows(g.Tables, t => Name(t.Name)); Groups(g.Groups); });
        static void Members(IEnumerable<HmiTagMemberInfo> members) => Rows(members, m => { Tag(m.Tag); Members(m.Members); });
        switch (value)
        {
            case HmiDeviceListInfo v: Rows(v.Devices, d => { Name(d.SoftwareName); Name(d.Kind); }); break;
            case HmiProjectLanguagesInfo v: Rows(v.Languages, Name); break;
            case HmiTagTableTreeInfo v: Rows(v.Tables, t => Name(t.Name)); Groups(v.Groups); break;
            case HmiTagListInfo v: Rows(v.Tags, Tag); break;
            case HmiTagDetailInfo v: Tag(v.Tag); Members(v.Members); break;
            case HmiAlarmListInfo v: Rows(v.Alarms, Alarm); break;
            case HmiAlarmDetailInfo v: Alarm(v.Alarm); break;
            case HmiConnectionListInfo v: Rows(v.Connections, c => Name(c.Name)); break;
            case HmiAlarmClassesInfo v: Rows(v.AlarmClasses, c => Name(c.Name)); break;
        }
    }
    private async Task<(IReadOnlyList<HmiReference> References, bool Complete)> ReadReferences(string? path, string hmi, string tag)
    {
        var result = await client.ReadCrossReferencesAsync(path, new() { Hmi = new() { HmiName = hmi, TagName = tag } }, CrossReferenceFilterNames.AllObjects, null).ConfigureAwait(false);
        if (!result.Success) throw new ReadFailure(result.FailureCategory ?? "worker_operation_failed", result.Error ?? "Tag references could not be read.");
        try
        {
            var report = CrossReferencePayloadContract.Decode(result);
            var references = new List<HmiReference>();
            void Visit(IEnumerable<CrossReferenceSourceInfo> sources)
            {
                foreach (var source in sources)
                {
                    foreach (var reference in source.References.Where(r => r.Locations.Any(l => l.ReferenceType == "UsedBy"))) references.Add(new(tag, reference.Name, reference.TypeName, reference.Path));
                    Visit(source.Children);
                }
            }
            Visit(report.Sources);
            return (references, report.IsComplete && report.OmittedSourceCount == 0 && report.OwnerQueryCount == report.SuccessfulOwnerQueryCount);
        }
        catch (JsonException) { throw new ReadFailure("protocol_error", "Tag references did not match their typed payload contract."); }
    }
}
