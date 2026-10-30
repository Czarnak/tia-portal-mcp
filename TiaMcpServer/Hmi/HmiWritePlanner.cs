using TiaMcpServer.Contracts.Worker;
using System.Text.Json;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.CrossReferences;
using TiaMcpServer.Json;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Hmi;

/// <summary>
/// Plans from target reads and delete consequences only; earlier items in the call are simulated, never re-read.
/// Cross-reference queries use each deleted tag's original planning name.
/// </summary>
public sealed class HmiWritePlanner(OpennessWorkerClient client)
{
    private const int MaxReferenceQueries = 200;
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    private readonly Dictionary<string, string> _resolved = new(StringComparer.Ordinal);
    private sealed class ReadFailure(string category, string message) : Exception(message)
    { public string Category { get; } = category; }
    private sealed class Context(HmiEvidence evidence)
    {
        public HmiEvidence Evidence = evidence;
        public readonly List<HmiWriteOperationRequest> History = new();
        public readonly HashSet<string> TableReads = new(Names);
        public readonly HashSet<string> ReferenceReads = new(Names);
        public HmiWorkingState Replay()
        {
            var state = HmiWorkingState.Create(Evidence);
            foreach (var item in History) state.Apply(item);
            return state;
        }
    }
    public string ResolvedHmiName(string operationId) => _resolved[operationId];

    public async Task<PlanResult<HmiWriteEffect>> PlanAsync(IReadOnlyList<HmiWriteOperationRequest> items)
    {
        _resolved.Clear();
        try
        {
            var devices = await Read<HmiDeviceListInfo>("list_hmi_devices", new());
            foreach (var item in items) _resolved[item.OperationId] = ResolveHmi(devices, item.HmiName);
            var contexts = new Dictionary<string, Context>(Names);
            foreach (var group in items.GroupBy(i => _resolved[i.OperationId], Names))
                contexts[group.Key] = new(await Load(group.Key, group.ToArray(), devices.IsComplete));

            // Target existence first: a call that cannot resolve its targets issues no consequence reads.
            if (Simulate(items, contexts).Select(s => s.Error).FirstOrDefault(e => e is not null) is { } targetError) return Failed(targetError);

            // Alarm triggers are delete-consequence evidence: UsedBy cross references do not identify them.
            foreach (var (hmi, context) in contexts)
            {
                if (!items.Any(i => Names.Equals(_resolved[i.OperationId], hmi) && i.Operation is "delete_tag" or "delete_tag_table")) continue;
                var (rows, alarmsComplete) = await Alarms(hmi);
                context.Evidence = context.Evidence with { Alarms = Merge(context.Evidence.Alarms, rows, a => a.Kind + "|" + a.Name), Complete = context.Evidence.Complete && alarmsComplete };
            }

            // Delete consequences depend on earlier items, so they are read against the simulation in caller order.
            var queryCount = 0;
            foreach (var item in items)
            {
                var context = contexts[_resolved[item.OperationId]];
                if (item.Operation is "delete_tag" or "delete_tag_table")
                {
                    var hmi = context.Evidence.HmiName;
                    if (item.Operation == "delete_tag_table" && context.Replay().OriginalTableName(item.TableName!) is { } table && context.TableReads.Add(table))
                    {
                        var (rows, complete) = await Tags(hmi, table);
                        context.Evidence = context.Evidence with { Tags = Merge(context.Evidence.Tags, rows, t => t.Name), Complete = context.Evidence.Complete && complete };
                    }
                    var state = context.Replay();
                    var resolution = state.Resolve(item);
                    if (resolution.Error is { } error) return Failed(error);
                    // Incomplete evidence can leave a missing target without a removal; it is already blocked.
                    var removed = resolution.Effect!.Removes?.Tags ?? Array.Empty<string>();
                    var originals = removed.Select(state.OriginalTagName).OfType<string>()
                        .Distinct(Names).Where(t => !context.ReferenceReads.Contains(t)).ToArray();
                    if (queryCount + originals.Length > MaxReferenceQueries) context.Evidence = context.Evidence with { Complete = false };
                    else foreach (var original in originals)
                    {
                        queryCount++;
                        context.ReferenceReads.Add(original);
                        var (references, complete) = await ReadReferences(hmi, original);
                        context.Evidence = context.Evidence with { References = context.Evidence.References.Concat(references).ToArray(), Complete = context.Evidence.Complete && complete };
                    }
                }
                context.History.Add(item);
            }

            // Resolve every effect from the same final observation, so later delete evidence informs earlier items.
            var plans = new List<ItemPlan<HmiWriteEffect>>();
            var guards = new Dictionary<string, IReadOnlyList<FiredGuard>>(StringComparer.Ordinal);
            foreach (var (item, resolution) in items.Zip(Simulate(items, contexts)))
            {
                if (resolution.Error is { } error) return Failed(error);
                plans.Add(ItemPlan<HmiWriteEffect>.Resolved(resolution.Effect!));
                guards[item.OperationId] = resolution.Guards;
            }
            return new(WritePlan<HmiWriteEffect>.Ok(plans), guards);
        }
        catch (ReadFailure failure) { return Failed(new(failure.Category, failure.Message)); }
    }

    /// <summary>Resolves each item in caller order against its HMI's current evidence, applying it before the next.</summary>
    private IEnumerable<HmiResolution> Simulate(IReadOnlyList<HmiWriteOperationRequest> items, Dictionary<string, Context> contexts)
    {
        var states = contexts.ToDictionary(pair => pair.Key, pair => HmiWorkingState.Create(pair.Value.Evidence), Names);
        foreach (var item in items)
        {
            var state = states[_resolved[item.OperationId]];
            yield return state.Resolve(item);
            state.Apply(item);
        }
    }

    private static IReadOnlyList<T> Merge<T>(IReadOnlyList<T> known, IEnumerable<T> rows, Func<T, string> key)
        => known.Concat(rows.Where(row => !known.Any(k => Names.Equals(key(k), key(row))))).ToArray();
    private static PlanResult<HmiWriteEffect> Failed(WriteToolError error) => new(WritePlan<HmiWriteEffect>.Fail(error.Category, error.Message), new Dictionary<string, IReadOnlyList<FiredGuard>>());
    private static string ResolveHmi(HmiDeviceListInfo devices, string? name)
    {
        var matches = devices.Devices.Where(d => name is null ? d.Kind == "unified" : Names.Equals(name, d.SoftwareName) || Names.Equals(name, d.DeviceName)).ToArray();
        if (matches.Length != 1) throw new ReadFailure(matches.Length == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous, "The HMI selector must resolve exactly one Unified HMI.");
        if (matches[0].Kind != "unified") throw new ReadFailure(WorkerFailureCategories.TargetKindUnsupported, "HMI writes require Unified software.");
        return matches[0].SoftwareName;
    }

    /// <summary>Target reads for one HMI: tables and update/delete targets.</summary>
    private async Task<HmiEvidence> Load(string hmi, IReadOnlyList<HmiWriteOperationRequest> items, bool complete)
    {
        var tables = new List<HmiTableEvidence>(); var tags = new List<HmiTagRowInfo>(); var alarms = new List<HmiAlarmRowInfo>();
        if (items.Any(i => i.Operation is "update_tag_table" or "delete_tag_table" or "update_tag" or "delete_tag"))
        {
            var tree = await Read<HmiTagTableTreeInfo>("list_tag_tables", new() { HmiName = hmi });
            void Flatten(IEnumerable<HmiTagTableInfo> ts, IEnumerable<HmiTagTableGroupInfo> gs, string? parent)
            {
                tables.AddRange(ts.Select(t => new HmiTableEvidence(t.Name, parent)));
                foreach (var group in gs) Flatten(group.Tables, group.Groups, parent is null ? group.Name : parent + "/" + group.Name);
            }
            Flatten(tree.Tables, tree.Groups, tree.GroupPath);
            complete &= tree.IsComplete;
        }
        // A target_not_found answer means absent at planning; the simulation may still create or rename that name.
        foreach (var name in items.Where(i => i.Operation is "update_tag" or "delete_tag").Select(i => i.TagName!).Distinct(Names))
        {
            if (await Read<HmiTagDetailInfo>("get_tag", new() { HmiName = hmi, TagName = name }, optional: true) is not { } detail) continue;
            tags.Add(detail.Tag); complete &= detail.IsComplete;
        }
        foreach (var target in items.Where(i => i.Operation is "update_alarm" or "delete_alarm").DistinctBy(i => (i.AlarmKind, i.AlarmName!.ToUpperInvariant())))
        {
            if (await Read<HmiAlarmDetailInfo>("get_alarm", new() { HmiName = hmi, AlarmName = target.AlarmName, AlarmKind = target.AlarmKind }, optional: true) is not { } detail) continue;
            alarms.Add(detail.Alarm); complete &= detail.IsComplete;
        }
        return new() { HmiName = hmi, Tables = tables, Tags = tags, Alarms = alarms, Complete = complete };
    }

    private Task<(List<HmiTagRowInfo> Rows, bool Complete)> Tags(string hmi, string? table)
        => Pages<HmiTagListInfo, HmiTagRowInfo>("list_tags", new() { HmiName = hmi, TableName = table }, p => (p.Tags, p.Page, p.IsComplete));
    private Task<(List<HmiAlarmRowInfo> Rows, bool Complete)> Alarms(string hmi)
        => Pages<HmiAlarmListInfo, HmiAlarmRowInfo>("list_alarms", new() { HmiName = hmi }, p => (p.Alarms, p.Page, p.IsComplete));
    private async Task<(List<TRow> Rows, bool Complete)> Pages<T, TRow>(string operation, HmiQueryInfo query, Func<T, (List<TRow> Rows, HmiPage Page, bool Complete)> unpack) where T : class
    {
        var rows = new List<TRow>(); var complete = true; int? total = null; string? snapshot = null;
        query.Offset = 0; query.Limit = 2000;
        while (true)
        {
            var (pageRows, page, pageComplete) = unpack(await Read<T>(operation, query));
            rows.AddRange(pageRows); complete &= pageComplete;
            if (page.Offset != query.Offset || page.Limit != 2000 || page.Total < rows.Count || total is not null && total != page.Total) return (rows, false);
            total = page.Total;
            if (page.NextOffset is null) return (rows, complete && rows.Count == page.Total && page.SnapshotId is null);
            if (pageRows.Count == 0 || page.NextOffset != rows.Count || page.NextOffset >= page.Total || string.IsNullOrEmpty(page.SnapshotId) || snapshot is not null && snapshot != page.SnapshotId) return (rows, false);
            snapshot = page.SnapshotId; query.SnapshotId = snapshot; query.Offset = page.NextOffset;
        }
    }

    private async Task<T> Read<T>(string operation, HmiQueryInfo query, bool optional = false) where T : class
    {
        var result = await client.ReadHmiAsync(HmiOperationCatalog.WorkerMethod(operation), query).ConfigureAwait(false);
        if (!result.Success)
        {
            if (optional && result.FailureCategory == WorkerFailureCategories.TargetNotFound) return null!;
            throw new ReadFailure(result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed, result.Error ?? "Required HMI evidence could not be read.");
        }
        try
        {
            var value = CanonicalJson.DeserializeWorkerPayload<T>(result.Payload);
            if (value is null) throw new JsonException();
            ValidateRows(value);
            return value;
        }
        catch (JsonException) { throw new ReadFailure(WorkerFailureCategories.ProtocolError, "Required HMI evidence did not match its typed payload contract."); }
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
            case HmiTagTableTreeInfo v: Rows(v.Tables, t => Name(t.Name)); Groups(v.Groups); break;
            case HmiTagListInfo v: Rows(v.Tags, Tag); break;
            case HmiTagDetailInfo v: Tag(v.Tag); Members(v.Members); break;
            case HmiAlarmListInfo v: Rows(v.Alarms, Alarm); break;
            case HmiAlarmDetailInfo v: Alarm(v.Alarm); break;
        }
    }
    private async Task<(IReadOnlyList<HmiReference> References, bool Complete)> ReadReferences(string hmi, string tag)
    {
        var result = await client.ReadCrossReferencesAsync(selector: new() { Hmi = new() { HmiName = hmi, TagName = tag } }, filter: CrossReferenceFilterNames.AllObjects, maxResults: null).ConfigureAwait(false);
        if (!result.Success) throw new ReadFailure(result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed, result.Error ?? "Tag references could not be read.");
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
        catch (JsonException) { throw new ReadFailure(WorkerFailureCategories.ProtocolError, "Tag references did not match their typed payload contract."); }
    }
}
