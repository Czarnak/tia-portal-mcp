using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>
/// Operations 7, 8 and 9: alarms and alarm classes. Reads enumerate compositions (never <c>Find</c>); each alarm
/// row is built in one pass over an explicit property list. Event texts are returned raw (spike S5). Audit
/// classes are listed by name only and <c>AuditClass</c> of an alarm is not read: the audit/GMP properties fail on
/// Comfort panels and one of them crashed TIA Portal on tags (spike crash finding). An unreadable composition
/// fails the item; an unreadable scalar property is null plus a message.
/// </summary>
public static class HmiAlarmReader
{
    private const string DiscreteKind = "discrete";
    private const string AnalogKind = "analog";

    private readonly record struct Entry(string Kind, AlarmBase Alarm, string Name);

    public static HmiAlarmListInfo ListAlarms(HmiSoftware software, string? alarmKind, string? language, int offset, int limit)
    {
        var (items, page) = HmiPager.Page(Entries(software, alarmKind), e => e.Name, offset, limit);
        var log = new HmiReadLog();
        var rows = items.Select(e => ReadRow(e, language, log)).ToList();
        return new HmiAlarmListInfo { IsComplete = log.IsComplete, Messages = log.Messages, Page = page, Alarms = rows };
    }

    public static HmiAlarmDetailInfo GetAlarm(HmiSoftware software, string alarmName, string alarmKind, string? language)
    {
        if (alarmKind != DiscreteKind && alarmKind != AnalogKind)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError, $"'{alarmKind}' is not an alarm kind. Use discrete or analog.");
        }

        var matches = Entries(software, alarmKind)
            .Where(e => string.Equals(e.Name, alarmName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.TargetNotFound,
                $"The {alarmKind} alarm '{alarmName}' was not found. Use list_alarms to see the alarms.");
        }

        if (matches.Count > 1)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.TargetAmbiguous, $"Several {alarmKind} alarms are named '{alarmName}'.");
        }

        var entry = matches[0];
        var log = new HmiReadLog();
        var alarm = entry.Alarm;
        string What(string property) => $"Property {property} of alarm '{entry.Name}'";
        var eventTexts = new Func<MultilingualText?>[]
        {
            () => alarm.EventText1, () => alarm.EventText2, () => alarm.EventText3,
            () => alarm.EventText4, () => alarm.EventText5, () => alarm.EventText6,
            () => alarm.EventText7, () => alarm.EventText8, () => alarm.EventText9,
        };
        var texts = eventTexts.Select((read, i) => ReadText(read, language, log, What($"EventText{i + 1}"))).ToList();

        return new HmiAlarmDetailInfo
        {
            Alarm = ReadRow(entry, language, log),
            EventText1 = texts[0],
            EventText2 = texts[1],
            EventText3 = texts[2],
            EventText4 = texts[3],
            EventText5 = texts[4],
            EventText6 = texts[5],
            EventText7 = texts[6],
            EventText8 = texts[7],
            EventText9 = texts[8],
            AlarmParameterTags = ReadParameterTags(alarm, log, What("AlarmParameterTags")),
            IsComplete = log.IsComplete,
            Messages = log.Messages,
        };
    }

    public static HmiAlarmClassesInfo ListAlarmClasses(HmiSoftware software)
    {
        var log = new HmiReadLog();
        var classes = HmiReadLog.Guard(() => software.AlarmClasses.ToList(), "The alarm classes")
            .Select(c => (Class: c, Name: HmiReadLog.Guard(() => c.Name, "An alarm class name")))
            .ToList();
        var audit = HmiReadLog.Guard(() => software.HmiAlarmAuditClass.ToList(), "The alarm audit classes")
            .Select(c => new HmiAuditClassInfo { Name = HmiReadLog.Guard(() => c.Name, "An alarm audit class name") })
            .ToList();
        var opcUa = HmiReadLog.Guard(() => software.OpcUaAlarmTypes.ToList(), "The OPC UA alarm types")
            .Select(t => (Type: t, Name: HmiReadLog.Guard(() => t.Name, "An OPC UA alarm type name")))
            .ToList();

        return new HmiAlarmClassesInfo
        {
            AlarmClasses = HmiPager.InNameOrder(classes, c => c.Name).Select(c => ReadClass(c.Class, c.Name, log)).ToList(),
            AuditClasses = HmiPager.InNameOrder(audit, a => a.Name).ToList(),
            OpcUaAlarmTypes = HmiPager.InNameOrder(opcUa, t => t.Name).Select(t => ReadOpcUaType(t.Type, t.Name, log)).ToList(),
            IsComplete = log.IsComplete,
            Messages = log.Messages,
        };
    }

    /// <summary>The alarms of the requested kind (both when null) with their names; a name or composition failure fails the item.</summary>
    private static List<Entry> Entries(HmiSoftware software, string? alarmKind)
    {
        var entries = new List<Entry>();
        if (alarmKind != AnalogKind)
        {
            foreach (var alarm in HmiReadLog.Guard(() => software.DiscreteAlarms.ToList(), "The discrete alarms"))
            {
                entries.Add(new Entry(DiscreteKind, alarm, HmiReadLog.Guard(() => alarm.Name, "A discrete alarm name")));
            }
        }

        if (alarmKind != DiscreteKind)
        {
            foreach (var alarm in HmiReadLog.Guard(() => software.AnalogAlarms.ToList(), "The analog alarms"))
            {
                entries.Add(new Entry(AnalogKind, alarm, HmiReadLog.Guard(() => alarm.Name, "An analog alarm name")));
            }
        }

        return entries;
    }

    private static HmiAlarmRowInfo ReadRow(Entry entry, string? language, HmiReadLog log)
    {
        var alarm = entry.Alarm;
        string What(string property) => $"Property {property} of alarm '{entry.Name}'";
        var row = new HmiAlarmRowInfo
        {
            Kind = entry.Kind,
            Name = entry.Name,
            AlarmClass = log.Try(() => alarm.AlarmClass, What("AlarmClass")),
            Priority = log.Try(() => (int?)alarm.Priority, What("Priority")),
            TriggerTag = log.Try(() => alarm.RaisedStateTag, What("RaisedStateTag")),
            EventText = ReadText(() => alarm.EventText, language, log, What("EventText")),
            InfoText = ReadText(() => alarm.InfoText, language, log, What("InfoText")),
        };

        if (alarm is HmiDiscreteAlarm discrete)
        {
            row.TriggerBit = log.Try(() => (long?)discrete.RaisedStateTagBitNumber, What("RaisedStateTagBitNumber"));
            row.TriggerMode = log.Try(() => discrete.TriggerMode.ToString(), What("TriggerMode"));
            row.AcknowledgmentControlTag = log.Try(() => discrete.AcknowledgmentControlTag, What("AcknowledgmentControlTag"));
            row.AcknowledgmentControlTagBit = log.Try(() => (int?)discrete.AcknowledgmentControlTagBitNumber, What("AcknowledgmentControlTagBitNumber"));
            row.AcknowledgmentStateTag = log.Try(() => discrete.AcknowledgmentStateTag, What("AcknowledgmentStateTag"));
            row.AcknowledgmentStateTagBit = log.Try(() => (int?)discrete.AcknowledgmentStateTagBitNumber, What("AcknowledgmentStateTagBitNumber"));
        }
        else if (alarm is HmiAnalogAlarm analog)
        {
            row.Condition = log.Try(() => analog.Condition.ToString(), What("Condition"));
            row.ConditionValue = ReadVariant(() => analog.ConditionValue, log, What("ConditionValue"));
        }

        return row;
    }

    private static HmiAlarmClassInfo ReadClass(HmiAlarmClass alarmClass, string name, HmiReadLog log)
    {
        string What(string property) => $"Property {property} of alarm class '{name}'";
        return new HmiAlarmClassInfo
        {
            Name = name,
            Id = log.Try(() => (long?)alarmClass.Id, What("Id")),
            Priority = log.Try(() => (int?)alarmClass.Priority, What("Priority")),
            IsSystem = log.Try(() => (bool?)alarmClass.IsSystem, What("IsSystem")),
            Log = log.Try(() => alarmClass.Log, What("Log")),
            StateMachine = log.Try(() => alarmClass.StateMachine.ToString(), What("StateMachine")),
            CommonAlarmClass = log.Try(() => alarmClass.CommonAlarmClass, What("CommonAlarmClass")),
        };
    }

    private static HmiOpcUaAlarmTypeInfo ReadOpcUaType(Siemens.Engineering.HmiUnified.HmiOpcUaAlarm.HmiOpcUaAlarmType type, string name, HmiReadLog log)
    {
        string What(string property) => $"Property {property} of OPC UA alarm type '{name}'";
        return new HmiOpcUaAlarmTypeInfo
        {
            Name = name,
            AlarmClass = log.Try(() => type.AlarmClass, What("AlarmClass")),
            Area = log.Try(() => type.Area, What("Area")),
            ConditionTypeId = log.Try(() => type.ConditionTypeId, What("ConditionTypeId")),
            Connection = log.Try(() => type.Connection, What("Connection")),
            NodeId = log.Try(() => type.NodeId, What("NodeId")),
        };
    }

    /// <summary>The raw per-culture texts, or null (plus a message) when the property could not be read.</summary>
    private static List<HmiText>? ReadText(Func<MultilingualText?> read, string? language, HmiReadLog log, string what)
    {
        MultilingualText? text;
        try
        {
            text = read();
        }
        catch (EngineeringException ex)
        {
            log.Recover(ex, $"{what} could not be read: {ex.Message}");
            return null;
        }

        var skipped = new List<string>();
        var texts = HmiTextMapper.Map(text, language, skipped).ToList();
        foreach (var message in skipped)
        {
            log.Fail(message);
        }

        return texts;
    }

    private static HmiVariant? ReadVariant(Func<object?> read, HmiReadLog log, string what)
    {
        object? value;
        try
        {
            value = read();
        }
        catch (EngineeringException ex)
        {
            log.Recover(ex, $"{what} could not be read: {ex.Message}");
            return null;
        }

        return HmiVariantMapper.Map(value);
    }

    /// <summary>The parameter-tag names in order; any other runtime shape, or a read failure, is null plus a message.</summary>
    private static List<string>? ReadParameterTags(AlarmBase alarm, HmiReadLog log, string what)
    {
        object? value;
        try
        {
            value = alarm.AlarmParameterTags;
        }
        catch (EngineeringException ex)
        {
            log.Recover(ex, $"{what} could not be read: {ex.Message}");
            return null;
        }

        var shape = new List<string>();
        var tags = HmiVariantMapper.MapStringList(value, shape, what);
        foreach (var message in shape)
        {
            log.Fail(message);
        }

        return tags?.ToList();
    }
}
