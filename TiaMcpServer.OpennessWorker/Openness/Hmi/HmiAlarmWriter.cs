using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;
namespace TiaMcpServer.OpennessWorker.Openness.Hmi;
public static class HmiAlarmWriter
{
    public static HmiWriteOutcomeInfo Create(HmiSoftware software, HmiWriteRequestInfo write)
        => write.AlarmKind switch
        {
            "discrete" => Create(write, () => software.DiscreteAlarms.Create(write.AlarmName!), () => software.DiscreteAlarms.Find(write.AlarmName!), alarm => alarm.Delete()),
            "analog" => Create(write, () => software.AnalogAlarms.Create(write.AlarmName!), () => software.AnalogAlarms.Find(write.AlarmName!), alarm => alarm.Delete()),
            _ => throw InvalidKind(),
        };
    private static HmiWriteOutcomeInfo Create<T>(HmiWriteRequestInfo write, Func<T> create, Func<T?> find, Action<T> delete) where T : AlarmBase
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = write.AlarmName! };
        T? created = null;
        try
        {
            outcome.FailedProperty = "Create";
            created = create();
            outcome.AppliedProperties.Add("Create");
            Apply(created, write, outcome);
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException)
        {
            outcome.Cleanup = "notNeeded";
            if (created is not null)
            {
                try
                {
                    var found = find();
                    if (found is not null)
                    {
                        if (!object.Equals(found, created)) throw new InvalidOperationException("Created alarm identity changed; cleanup refused.");
                        delete(found);
                        outcome.Cleanup = "deleted";
                    }
                }
                catch (Exception cleanup) { outcome.Cleanup = "failed"; outcome.CleanupMessage = cleanup.Message; }
            }
            throw HmiTagWriter.Failure(ex, outcome);
        }
    }
    public static HmiWriteOutcomeInfo Update(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = write.NewName ?? write.AlarmName! };
        var alarm = Find(software, write);
        try
        {
            if (write.NewName is not null)
            {
                if (alarm is HmiDiscreteAlarm discrete) HmiTagWriter.Set(outcome, "Name", () => discrete.Name = write.NewName);
                else HmiTagWriter.Set(outcome, "Name", () => ((HmiAnalogAlarm)alarm).Name = write.NewName);
            }
            Apply(alarm, write, outcome);
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw HmiTagWriter.Failure(ex, outcome); }
    }
    public static HmiWriteOutcomeInfo Delete(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = write.AlarmName! };
        var alarm = Find(software, write);
        try
        {
            if (alarm is HmiDiscreteAlarm discrete) HmiTagWriter.Set(outcome, "Delete", discrete.Delete);
            else HmiTagWriter.Set(outcome, "Delete", ((HmiAnalogAlarm)alarm).Delete);
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw HmiTagWriter.Failure(ex, outcome); }
    }
    private static AlarmBase Find(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var matches = write.AlarmKind switch
        {
            "discrete" => HmiReadLog.Guard(() => software.DiscreteAlarms.ToList(), "The discrete alarms")
                .Where(a => string.Equals(HmiReadLog.Guard(() => a.Name, "An alarm name"), write.AlarmName, StringComparison.OrdinalIgnoreCase)).Cast<AlarmBase>().ToList(),
            "analog" => HmiReadLog.Guard(() => software.AnalogAlarms.ToList(), "The analog alarms")
                .Where(a => string.Equals(HmiReadLog.Guard(() => a.Name, "An alarm name"), write.AlarmName, StringComparison.OrdinalIgnoreCase)).Cast<AlarmBase>().ToList(),
            _ => throw InvalidKind(),
        };
        if (matches.Count == 0) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The {write.AlarmKind} alarm '{write.AlarmName}' was not found.");
        if (matches.Count > 1) throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"Several {write.AlarmKind} alarms are named '{write.AlarmName}'.");
        return matches[0];
    }
    private static WorkerOperationException InvalidKind() => new(WorkerFailureCategories.ValidationError, "Use discrete or analog for alarmKind.");
    private static void Apply(AlarmBase alarm, HmiWriteRequestInfo write, HmiWriteOutcomeInfo outcome)
    {
        if (write.TriggerTag is not null) HmiTagWriter.Set(outcome, "RaisedStateTag", () => alarm.RaisedStateTag = write.TriggerTag);
        if (alarm is HmiDiscreteAlarm discrete)
        {
            if (write.BitNumber.HasValue) HmiTagWriter.Set(outcome, "RaisedStateTagBitNumber", () => discrete.RaisedStateTagBitNumber = checked((uint)write.BitNumber.Value));
            if (write.TriggerMode is not null) HmiTagWriter.Set(outcome, "TriggerMode", () => discrete.TriggerMode = (HmiDiscreteAlarmTriggerMode)Enum.Parse(typeof(HmiDiscreteAlarmTriggerMode), write.TriggerMode));
        }
        else
        {
            var analog = (HmiAnalogAlarm)alarm;
            if (write.Condition is not null) HmiTagWriter.Set(outcome, "Condition", () => analog.Condition = (HmiAlarmCondition)Enum.Parse(typeof(HmiAlarmCondition), write.Condition));
            if (write.ConditionValueInt32.HasValue) HmiTagWriter.Set(outcome, "ConditionValue", () => analog.ConditionValue = write.ConditionValueInt32.Value);
            else if (write.ConditionValueDouble.HasValue) HmiTagWriter.Set(outcome, "ConditionValue", () => analog.ConditionValue = write.ConditionValueDouble.Value);
        }
        if (write.AlarmClass is not null) HmiTagWriter.Set(outcome, "AlarmClass", () => alarm.AlarmClass = write.AlarmClass);
        if (write.Priority.HasValue) HmiTagWriter.Set(outcome, "Priority", () => alarm.Priority = checked((byte)write.Priority.Value));
        if (write.EventText is not null) HmiTagWriter.Set(outcome, "EventText", () => HmiTextMapper.Write(alarm.EventText, write.EventText));
        if (write.InfoText is not null) HmiTagWriter.Set(outcome, "InfoText", () => HmiTextMapper.Write(alarm.InfoText, write.InfoText));
        outcome.FailedProperty = null;
    }
}
