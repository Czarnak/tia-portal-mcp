using System.Text.Json;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Hmi;

/// <summary>Stateful synthetic HMI writes and public read-back; no Siemens objects are loaded.</summary>
internal sealed class HmiWriteRoundtripScenario
{
    private readonly List<string> _tables = ["Existing"];
    private readonly List<HmiTagRowInfo> _tags = [];
    private readonly List<HmiAlarmRowInfo> _alarms = [];
    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string Ok(object value) => JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = WorkerJson.SerializePayload(value) }, WorkerJson.Envelope);
    private static string Missing() => JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = "target_not_found", Error = "Synthetic object not found." }, WorkerJson.Envelope);

    public string Handle(string line)
    {
        var r = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        var q = r.HmiQuery;
        switch (r.Method)
        {
            case "read_hardware_config": return Ok(new HardwareConfigInfo());
            case "read_cross_references": return Ok(new CrossReferenceReport { IsComplete = true, OwnerQueryCount = 1, SuccessfulOwnerQueryCount = 1, Target = r.CrossReferenceSelector! });
            case "hmi_list_hmi_devices": return Ok(new HmiDeviceListInfo { Devices = [new() { SoftwareName = "H", DeviceName = "Device", Kind = "unified" }] });
            case "hmi_list_project_languages": return Ok(new HmiProjectLanguagesInfo { Languages = ["en-US", "pl-PL"] });
            case "hmi_list_tag_tables": return Ok(new HmiTagTableTreeInfo { Tables = _tables.Select(t => new HmiTagTableInfo { Name = t, TagCount = _tags.Count(tag => Same(tag.Table, t)) }).ToList() });
            case "hmi_list_tags": return Ok(new HmiTagListInfo { Tags = _tags.Where(t => q?.TableName is null || Same(t.Table, q.TableName)).ToList(), Page = new(0, 2000, _tags.Count(t => q?.TableName is null || Same(t.Table, q.TableName)), null) });
            case "hmi_list_connections": return Ok(new HmiConnectionListInfo { Connections = [new() { Name = "Connection" }] });
            case "hmi_list_alarm_classes": return Ok(new HmiAlarmClassesInfo { AlarmClasses = [new() { Name = "Alarm" }] });
            case "hmi_list_alarms": return Ok(new HmiAlarmListInfo { Alarms = _alarms.ToList(), Page = new(0, 2000, _alarms.Count, null) });
            case "hmi_get_tag": return _tags.FirstOrDefault(t => Same(t.Name, q!.TagName)) is { } tag ? Ok(new HmiTagDetailInfo { Tag = tag }) : Missing();
            case "hmi_get_alarm": return _alarms.FirstOrDefault(a => Same(a.Name, q!.AlarmName) && a.Kind == q.AlarmKind) is { } alarm ? Ok(new HmiAlarmDetailInfo { Alarm = alarm }) : Missing();
            case "hmi_validate": return Ok(new HmiValidationInfo { Page = new(0, 2000, 1, null), Scanned = 1, Clean = 1 });
        }
        var w = r.HmiWrite!;
        if (w is null || w.HmiName != "H") return Missing();
        var outcome = new HmiWriteOutcomeInfo { ObjectName = w.NewName ?? w.TagName ?? w.AlarmName ?? w.TableName! };
        switch (r.Method)
        {
            case "hmi_create_tag_table": _tables.Add(w.TableName!); outcome.AppliedProperties.Add("Name"); break;
            case "hmi_update_tag_table":
                var index = _tables.FindIndex(t => Same(t, w.TableName));
                if (index < 0) return Missing();
                _tables[index] = w.NewName!;
                foreach (var tag in _tags.Where(t => Same(t.Table, w.TableName))) tag.Table = w.NewName;
                outcome.AppliedProperties.Add("Name"); break;
            case "hmi_delete_tag_table": _tables.RemoveAll(t => Same(t, w.TableName)); _tags.RemoveAll(t => Same(t.Table, w.TableName)); break;
            case "hmi_create_tag":
            case "hmi_update_tag":
                var created = r.Method == "hmi_create_tag";
                var row = created ? new HmiTagRowInfo { Name = w.TagName!, Table = w.TableName, Connection = "<Internal tag>" } : _tags.FirstOrDefault(t => Same(t.Name, w.TagName));
                if (row is null) return Missing();
                if (created) { _tags.Add(row); outcome.AppliedProperties.Add("Name"); }
                if (w.NewName is not null)
                {
                    foreach (var alarm in _alarms.Where(a => a.TriggerTag is not null && (Same(a.TriggerTag, row.Name) || a.TriggerTag.StartsWith(row.Name + ".", StringComparison.OrdinalIgnoreCase))))
                        alarm.TriggerTag = w.NewName + alarm.TriggerTag![row.Name.Length..];
                    row.Name = w.NewName; outcome.AppliedProperties.Add("Name");
                }
                if (w.Connection is not null) { row.Connection = w.Connection; outcome.AppliedProperties.Add("Connection"); }
                if (w.PlcTag == "MalformedOutcome")
                {
                    outcome.ObjectName = "";
                    outcome.CleanupMessage = "malformed evidence must not be echoed";
                    return JsonSerializer.Serialize(new WorkerResponse { Success = false, Error = "Synthetic setter failed after create.", FailureCategory = "worker_operation_failed", HmiWriteOutcome = outcome, Warnings = [new string('x', 61000)] }, WorkerJson.Envelope);
                }
                if (w.PlcTag is "Missing" or "CleanupFails")
                {
                    outcome.FailedProperty = "PlcTag";
                    if (created)
                    {
                        outcome.Cleanup = w.PlcTag == "Missing" ? "deleted" : "failed";
                        if (outcome.Cleanup == "deleted") _tags.Remove(row); else outcome.CleanupMessage = "Synthetic cleanup identity changed.";
                    }
                    return JsonSerializer.Serialize(new WorkerResponse { Success = false, Error = "Synthetic PLC tag rejected.", FailureCategory = "worker_operation_failed", HmiWriteOutcome = outcome }, WorkerJson.Envelope);
                }
                if (w.PlcTag is not null) { row.PlcTag = w.PlcTag; row.PlcName = "PLC"; row.DataType = "Bool"; outcome.AppliedProperties.Add("PlcTag"); }
                if (w.DataType is not null) { row.DataType = w.DataType; outcome.AppliedProperties.Add("DataType"); }
                if (w.AcquisitionCycle is not null) row.AcquisitionCycle = w.AcquisitionCycle;
                if (w.Comment is not null) row.Comment = w.Comment;
                if (w.DisplayName is not null) row.DisplayName = w.DisplayName;
                break;
            case "hmi_delete_tag": _tags.RemoveAll(t => Same(t.Name, w.TagName)); break;
            case "hmi_create_alarm":
            case "hmi_update_alarm":
                var alarmRow = r.Method == "hmi_create_alarm" ? new HmiAlarmRowInfo { Name = w.AlarmName!, Kind = w.AlarmKind! } : _alarms.FirstOrDefault(a => Same(a.Name, w.AlarmName) && a.Kind == w.AlarmKind);
                if (alarmRow is null) return Missing();
                if (r.Method == "hmi_create_alarm") _alarms.Add(alarmRow);
                if (w.NewName is not null) alarmRow.Name = w.NewName;
                if (w.TriggerTag is not null) alarmRow.TriggerTag = w.TriggerTag;
                if (w.BitNumber is not null) alarmRow.TriggerBit = w.BitNumber;
                if (w.TriggerMode is not null) alarmRow.TriggerMode = w.TriggerMode;
                if (w.Condition is not null) alarmRow.Condition = w.Condition;
                if (w.ConditionValueInt32 is { } integer) alarmRow.ConditionValue = new("Int32", JsonSerializer.SerializeToElement(integer));
                if (w.ConditionValueDouble is { } number) alarmRow.ConditionValue = new("Double", JsonSerializer.SerializeToElement(number));
                if (w.AlarmClass is not null) alarmRow.AlarmClass = w.AlarmClass;
                if (w.Priority is not null) alarmRow.Priority = w.Priority;
                if (w.EventText is not null) alarmRow.EventText = w.EventText;
                if (w.InfoText is not null) alarmRow.InfoText = w.InfoText;
                // A concurrent editor change makes both late delete-info messages differ from planning.
                if (w.AlarmName == "LateDrift")
                {
                    _tags.Add(new() { Name = "ConcurrentTag", Table = "NewTable", Connection = "<Internal tag>" });
                    _alarms.Add(new() { Name = "ConcurrentAlarm", Kind = "discrete", TriggerTag = "ConcurrentTag" });
                }
                outcome.AppliedProperties.Add("Name"); break;
            case "hmi_delete_alarm": _alarms.RemoveAll(a => Same(a.Name, w.AlarmName) && a.Kind == w.AlarmKind); break;
            default: return Missing();
        }
        return Ok(outcome);
    }
}
