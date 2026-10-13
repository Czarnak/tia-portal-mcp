using System.Globalization;
using System.Text.Json;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Hmi;

/// <summary>Checks final write expectations with public, strictly typed HMI reads.</summary>
public sealed class HmiWriteVerifier(OpennessWorkerClient client, IReadOnlyList<HmiWriteOperationRequest> items,
    IReadOnlyDictionary<string, HmiWriteEffect> effects)
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> NeverRan = new(StringComparer.Ordinal)
    { "validation_error", "target_not_found", "target_ambiguous", "state_changed", "guard_blocked", "binding_conflict", "access_denied" };
    private sealed class ReadFailure(string message) : Exception(message);
    private sealed class Expectation(HmiTargetIdentity target, string owner)
    {
        public HmiTargetIdentity Target = target;
        public string Owner = owner;
        public bool Absent;
        public bool Observe;
        public string? AlternateName;
        public string? Message;
        public readonly Dictionary<string, string?> Fields = new(Names);
        public readonly HashSet<string> Observed = new(Names);
    }

    public async Task<HmiWriteVerification> VerifyAsync(string? projectPath, StructuredOperationBatch batch)
    {
        var results = new List<HmiOperationVerification>();
        var outcomes = batch.Operations.ToDictionary(o => o.OperationId, StringComparer.Ordinal);
        var expectations = new List<Expectation>();
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!outcomes.TryGetValue(item.OperationId, out var outcome) || outcome.Status is not (OperationBatchStatus.Succeeded or OperationBatchStatus.Failed)) continue;
            if (outcome.Failure is { } refusal && NeverRan.Contains(refusal.Category))
            {
                results.Add(new(item.OperationId, true, "skipped", null, null, refusal.Message));
                continue;
            }
            try { Accumulate(item, outcome, expectations, owners); }
            catch (JsonException) { results.Add(new(item.OperationId, false, "protocol_error", null, null, "The attempted item's outcome did not match the typed HMI write contract.")); }
        }
        foreach (var item in items)
            if (owners.TryGetValue(item.OperationId, out var later) && !expectations.Any(e => e.Owner == item.OperationId))
                results.Add(new(item.OperationId, true, "superseded", null, null, $"Final state is verified by operation '{later}'."));
        foreach (var expected in expectations)
        {
            try { await VerifyOne(projectPath, expected, results).ConfigureAwait(false); }
            catch (ReadFailure ex) { results.Add(new(expected.Owner, false, "postcondition_failed", null, null, ex.Message)); }
        }
        return new(results.All(r => r.Success), results, null);
    }

    private void Accumulate(HmiWriteOperationRequest item, StructuredOperationItem outcome,
        List<Expectation> expectations, Dictionary<string, string> owners)
    {
        var target = effects[item.OperationId].Target;
        var failed = outcome.Failure is not null;
        var result = outcome.Result is { } payload
            ? CanonicalJson.DeserializeWorkerPayload<HmiWriteOutcomeInfo>(payload.GetRawText()) ?? throw new JsonException() : null;
        if (result is not null && (result.AppliedProperties is null || result.AppliedProperties.Any(p => p is null))) throw new JsonException();
        bool Applied(string field) => !failed || result?.AppliedProperties.Contains(Property(field)) == true;
        var create = item.Operation.StartsWith("create_", StringComparison.Ordinal);
        var delete = item.Operation.StartsWith("delete_", StringComparison.Ordinal);
        void Supersede(string previousOwner)
        {
            if (previousOwner == item.OperationId) return;
            foreach (var previous in owners.Where(p => p.Value == previousOwner).Select(p => p.Key).ToArray()) owners[previous] = item.OperationId;
            owners[previousOwner] = item.OperationId;
        }
        var entry = expectations.LastOrDefault(e => Same(e.Target, target));
        if (entry is null) { entry = new(target, item.OperationId); expectations.Add(entry); }
        else
        {
            Supersede(entry.Owner);
            entry.Owner = item.OperationId;
        }
        if (create || delete && !failed) { entry.Fields.Clear(); entry.Observed.Clear(); }
        entry.Absent = delete && !failed || create && failed;
        entry.Observe = failed;
        entry.Message = failed ? $"{outcome.Failure!.Category}: {outcome.Failure.Message}; cleanup={result?.Cleanup ?? "unknown"}; {result?.CleanupMessage}" : null;
        var oldName = target.Name;
        var name = item.NewName is not null && Applied("name") ? item.NewName : oldName;
        entry.Target = target with { Name = name };
        entry.AlternateName = failed && result is null ? item.NewName : null;
        if (!entry.Absent && !delete)
        {
            entry.Fields["name"] = name;
            if (target.Kind == "Tag") entry.Fields["table"] = target.TableName;
            var requested = Requested(item, target.HmiName);
            foreach (var field in requested)
                if (Applied(field.Key)) entry.Fields[field.Key] = field.Value;
                else entry.Observed.Add(field.Key);
            if (entry.AlternateName is not null) entry.Fields.Remove("name");
        }
        if (name != oldName)
        {
            // An applied rename replaces the deleted destination's incarnation, not the source's fields.
            foreach (var destination in expectations.Where(e => e.Absent && e != entry && Same(e.Target, entry.Target)).ToArray())
            { Supersede(destination.Owner); expectations.Remove(destination); }
            foreach (var other in expectations.Where(e => Names.Equals(e.Target.HmiName, target.HmiName) && !e.Absent))
            {
                if (target.Kind == "TagTable" && other.Target.Kind == "Tag" && other.Fields.TryGetValue("table", out var table) && Names.Equals(table, oldName))
                { other.Fields["table"] = name; other.Target = other.Target with { TableName = name }; }
                if (target.Kind == "Tag" && other.Target.Kind == "Alarm" && other.Fields.TryGetValue("triggerTag", out var trigger) && trigger is not null
                    && (Names.Equals(trigger, oldName) || trigger.StartsWith(oldName + ".", StringComparison.OrdinalIgnoreCase)))
                    other.Fields["triggerTag"] = name + trigger[oldName.Length..];
            }
        }
        if (delete && !failed && effects[item.OperationId].Removes is { } removal && target.Kind == "TagTable")
        {
            foreach (var tag in removal.Tags)
            {
                var identity = new HmiTargetIdentity("Tag", target.HmiName, tag, null, target.Name, target.GroupPath);
                var member = expectations.LastOrDefault(e => Same(e.Target, identity));
                if (member is null) { member = new(identity, item.OperationId); expectations.Add(member); }
                else { Supersede(member.Owner); member.Owner = item.OperationId; }
                member.Absent = true; member.Fields.Clear(); member.Observed.Clear();
            }
        }
    }

    private static bool Same(HmiTargetIdentity a, HmiTargetIdentity b)
        => a.Kind == b.Kind && a.AlarmKind == b.AlarmKind && Names.Equals(a.HmiName, b.HmiName) && Names.Equals(a.Name, b.Name);
    private static string Property(string field) => field.Split('[', '.')[0] switch
    { "triggerTag" => "RaisedStateTag", "bitNumber" => "RaisedStateTagBitNumber", var f => char.ToUpperInvariant(f[0]) + f[1..] };
    private static string? Scalar(object? value) => value switch
    { null => null, JsonElement j => j.ValueKind == JsonValueKind.String ? j.GetString() : j.GetRawText(), IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => value.ToString() };
    private static Dictionary<string, string?> Requested(HmiWriteOperationRequest item, string hmi)
    {
        var r = HmiWriteCatalog.ToWorkerRequest(item, hmi);
        var fields = new Dictionary<string, string?>(Names);
        void Add(string name, object? value) { if (value is not null) fields[name] = Scalar(value); }
        void Texts(string name, List<HmiText>? texts) { foreach (var text in texts ?? []) fields[$"{name}[{text.Culture}]"] = text.Text; }
        Add("connection", r.Connection); Add("plcTag", r.PlcTag); Add("dataType", r.DataType); Add("acquisitionCycle", r.AcquisitionCycle);
        Add("triggerTag", r.TriggerTag); Add("bitNumber", r.BitNumber); Add("triggerMode", r.TriggerMode); Add("condition", r.Condition);
        Add("alarmClass", r.AlarmClass); Add("priority", r.Priority);
        if (r.ConditionValueInt32 is { } integer) { Add("conditionValue", integer); Add("conditionValue.type", "System.Int32"); }
        if (r.ConditionValueDouble is { } number) { Add("conditionValue", number); Add("conditionValue.type", "System.Double"); }
        Texts("comment", r.Comment); Texts("displayName", r.DisplayName); Texts("eventText", r.EventText); Texts("infoText", r.InfoText);
        return fields;
    }

    private async Task VerifyOne(string? path, Expectation expected, List<HmiOperationVerification> checks)
    {
        var target = expected.Target;
        var actual = await Observe(path, target).ConfigureAwait(false);
        if (actual is null && expected.AlternateName is { } alternate)
            actual = await Observe(path, target with { Name = alternate }).ConfigureAwait(false);
        void Check(string field, string? wanted, string? found, bool observe = false, string? message = null)
            => checks.Add(new(expected.Owner, observe || wanted == found, field, wanted, found, message ?? (wanted == found || observe ? expected.Message : "postcondition_failed: observed value differs from the final expectation.")));
        if (expected.Absent)
        { Check("absent", "absent", actual is null ? "absent" : "present"); return; }
        if (actual is null)
        { Check(expected.Observe ? "observed" : "exists", expected.Observe ? null : "present", "absent", expected.Observe); return; }
        if (expected.Observe) Check("observed", null, "present", true);
        foreach (var field in expected.Fields) Check(field.Key, field.Value, actual.GetValueOrDefault(field.Key));
        foreach (var field in expected.Observed) Check("observed." + field, null, actual.GetValueOrDefault(field), true);
        foreach (var field in target.Kind switch { "TagTable" => new[] { "tagCount" }, "Tag" => new[] { "plcName", "dataType" }, _ => new[] { "triggerBitAddress", "triggerAddress" } })
            if (!expected.Fields.ContainsKey(field)) Check(field, null, actual.GetValueOrDefault(field), true);
        if (target.Kind == "TagTable")
        { Check("groupPath", target.GroupPath, actual.GetValueOrDefault("groupPath")); return; }
        var query = new HmiQueryInfo { HmiName = target.HmiName, Category = target.Kind == "Tag" ? "tags" : "alarms", Name = actual["name"], Offset = 0, Limit = 2000 };
        var kind = target.Kind == "Tag" ? "HmiTag" : target.AlarmKind == "discrete" ? "HmiDiscreteAlarm" : "HmiAnalogAlarm";
        var offset = 0; string? snapshot = null; int? total = null;
        do
        {
            var validation = await Read<HmiValidationInfo>(path, "validate", query).ConfigureAwait(false) ?? throw new ReadFailure("Validation returned no result.");
            if (!validation.IsComplete || validation.Findings is null || validation.NotValidatable is null || validation.Page is not { } page
                || page.Offset != offset || page.Limit != 2000 || page.Total <= 0 || validation.Scanned <= 0
                || page.Total < offset + validation.Scanned || total is not null && total != page.Total)
                throw new ReadFailure("Validation evidence is incomplete.");
            total = page.Total;
            if (validation.NotValidatable.Contains(kind)) throw new ReadFailure("The written object could not be validated.");
            foreach (var finding in validation.Findings)
            {
                if (finding is null || finding.Results is null) throw new ReadFailure("Invalid validation finding.");
                if (finding.ObjectKind != kind || !Names.Equals(finding.Name, actual["name"])) continue;
                foreach (var result in finding.Results)
                {
                    if (result is null || result.Errors is null || result.Warnings is null) throw new ReadFailure("Invalid validation result.");
                    foreach (var error in result.Errors) checks.Add(new(expected.Owner, false, "postcondition_failed", null, null, error));
                    foreach (var warning in result.Warnings) checks.Add(new(expected.Owner, true, "warning", null, null, warning));
                }
            }
            if (page.NextOffset is not { } next)
            { if (offset + validation.Scanned != page.Total || page.SnapshotId is not null) throw new ReadFailure("Validation evidence is incomplete."); break; }
            if (validation.Scanned <= 0 || next != offset + validation.Scanned || next >= page.Total || string.IsNullOrEmpty(page.SnapshotId) || snapshot is not null && snapshot != page.SnapshotId)
                throw new ReadFailure("Validation continuation is invalid.");
            offset = next; snapshot = page.SnapshotId; query.Offset = offset; query.SnapshotId = snapshot;
        } while (true);
    }

    private async Task<Dictionary<string, string?>?> Observe(string? path, HmiTargetIdentity target)
    {
        var values = new Dictionary<string, string?>(Names);
        void Add(string field, object? value) => values[field] = Scalar(value);
        void Texts(string field, List<HmiText>? texts)
        {
            foreach (var text in texts ?? [])
            {
                if (text is null || string.IsNullOrEmpty(text.Culture) || !values.TryAdd($"{field}[{text.Culture}]", text.Text))
                    throw new ReadFailure("Invalid multilingual read-back evidence.");
            }
        }
        var query = new HmiQueryInfo { HmiName = target.HmiName };
        if (target.Kind == "TagTable")
        {
            var tree = await Read<HmiTagTableTreeInfo>(path, "list_tag_tables", query).ConfigureAwait(false) ?? throw new ReadFailure("Missing table inventory.");
            if (!tree.IsComplete || tree.Tables is null || tree.Groups is null) throw new ReadFailure("Incomplete table inventory.");
            var tables = new List<(HmiTagTableInfo Table, string? Group)>();
            void Walk(List<HmiTagTableInfo> rows, List<HmiTagTableGroupInfo> groups, string? group)
            {
                foreach (var table in rows)
                { if (table is null || string.IsNullOrEmpty(table.Name) || table.TagCount < 0) throw new ReadFailure("Invalid table inventory."); tables.Add((table, group)); }
                foreach (var child in groups)
                { if (child is null || child.Tables is null || child.Groups is null || string.IsNullOrEmpty(child.Name)) throw new ReadFailure("Invalid table group."); Walk(child.Tables, child.Groups, group is null ? child.Name : group + "/" + child.Name); }
            }
            Walk(tree.Tables, tree.Groups, tree.GroupPath);
            var matches = tables.Where(t => Names.Equals(t.Table.Name, target.Name)).ToArray();
            if (matches.Length == 0) return null;
            if (matches.Length != 1) throw new ReadFailure("Table read-back is ambiguous.");
            Add("name", matches[0].Table.Name); Add("groupPath", matches[0].Group); Add("tagCount", matches[0].Table.TagCount);
        }
        else if (target.Kind == "Tag")
        {
            query.TagName = target.Name;
            var detail = await Read<HmiTagDetailInfo>(path, "get_tag", query, true).ConfigureAwait(false);
            if (detail is null) return null;
            if (!detail.IsComplete || detail.Tag is not { } tag || string.IsNullOrEmpty(tag.Name)) throw new ReadFailure("Incomplete tag read-back.");
            Add("name", tag.Name); Add("table", tag.Table); Add("connection", tag.Connection); Add("plcTag", tag.PlcTag); Add("plcName", tag.PlcName);
            Add("dataType", tag.DataType); Add("acquisitionCycle", tag.AcquisitionCycle); Texts("comment", tag.Comment); Texts("displayName", tag.DisplayName);
        }
        else
        {
            query.AlarmName = target.Name; query.AlarmKind = target.AlarmKind;
            var detail = await Read<HmiAlarmDetailInfo>(path, "get_alarm", query, true).ConfigureAwait(false);
            if (detail is null) return null;
            if (!detail.IsComplete || detail.Alarm is not { } alarm || string.IsNullOrEmpty(alarm.Name) || alarm.Kind != target.AlarmKind) throw new ReadFailure("Incomplete or wrong-kind alarm read-back.");
            Add("name", alarm.Name); Add("triggerTag", alarm.TriggerTag); Add("bitNumber", alarm.TriggerBit); Add("triggerMode", alarm.TriggerMode);
            Add("condition", alarm.Condition);
            if (alarm.ConditionValue?.Value is { } number)
            {
                if (number.ValueKind != JsonValueKind.Number || !number.TryGetDouble(out var value) || !double.IsFinite(value))
                    throw new ReadFailure("Invalid numeric condition-value read-back.");
                Add("conditionValue", value);
            }
            Add("conditionValue.type", alarm.ConditionValue?.Type);
            Add("alarmClass", alarm.AlarmClass); Add("priority", alarm.Priority); Add("triggerBitAddress", alarm.TriggerBitAddress); Add("triggerAddress", alarm.TriggerAddress);
            Texts("eventText", alarm.EventText); Texts("infoText", alarm.InfoText);
        }
        return values;
    }

    private async Task<T?> Read<T>(string? path, string operation, HmiQueryInfo query, bool optional = false) where T : class
    {
        var result = await client.ReadHmiAsync(HmiOperationCatalog.WorkerMethod(operation), query, path).ConfigureAwait(false);
        if (!result.Success)
        {
            if (optional && result.FailureCategory == "target_not_found") return null;
            throw new ReadFailure(result.Error ?? "HMI read-back failed.");
        }
        try { return CanonicalJson.DeserializeWorkerPayload<T>(result.Payload) ?? throw new JsonException(); }
        catch (JsonException) { throw new ReadFailure("protocol_error: HMI read-back did not match its typed payload contract."); }
    }
}
