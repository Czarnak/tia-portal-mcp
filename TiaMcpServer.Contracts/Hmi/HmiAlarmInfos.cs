namespace TiaMcpServer.Contracts.Hmi;

/// <summary>Result of <c>list_alarms</c>: one page of discrete and analog alarm rows ordered by name. Texts are raw (markup kept).</summary>
public class HmiAlarmListInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public HmiPage Page { get; set; } = new HmiPage(0, 0, 0, null);

    public List<HmiAlarmRowInfo> Alarms { get; set; } = new List<HmiAlarmRowInfo>();
}

/// <summary>
/// One alarm. <see cref="Kind"/> is <c>discrete</c> or <c>analog</c>; members that do not apply to the kind
/// (trigger mode and acknowledgment tags for analog, condition for discrete) are null, as is any unreadable property.
/// </summary>
public class HmiAlarmRowInfo
{
    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? AlarmClass { get; set; }

    public int? Priority { get; set; }

    public string? TriggerTag { get; set; }

    /// <summary>Discrete only: the bit of the trigger tag.</summary>
    public long? TriggerBit { get; set; }

    /// <summary>Discrete only.</summary>
    public string? TriggerMode { get; set; }

    /// <summary>Analog only.</summary>
    public string? Condition { get; set; }

    /// <summary>Analog only.</summary>
    public HmiVariant? ConditionValue { get; set; }

    /// <summary>Discrete only; <c>"&lt;No tag&gt;"</c> when unset.</summary>
    public string? AcknowledgmentControlTag { get; set; }

    public int? AcknowledgmentControlTagBit { get; set; }

    public string? AcknowledgmentStateTag { get; set; }

    public int? AcknowledgmentStateTagBit { get; set; }

    public List<HmiText>? EventText { get; set; } = new List<HmiText>();

    public List<HmiText>? InfoText { get; set; } = new List<HmiText>();
}

/// <summary>Result of <c>get_alarm</c>: the list row plus event texts 1 to 9 and the alarm parameter tags.</summary>
public class HmiAlarmDetailInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public HmiAlarmRowInfo Alarm { get; set; } = new HmiAlarmRowInfo();

    public List<HmiText>? EventText1 { get; set; } = new List<HmiText>();

    public List<HmiText>? EventText2 { get; set; } = new List<HmiText>();

    public List<HmiText>? EventText3 { get; set; } = new List<HmiText>();

    public List<HmiText>? EventText4 { get; set; } = new List<HmiText>();

    public List<HmiText>? EventText5 { get; set; } = new List<HmiText>();

    public List<HmiText>? EventText6 { get; set; } = new List<HmiText>();

    public List<HmiText>? EventText7 { get; set; } = new List<HmiText>();

    public List<HmiText>? EventText8 { get; set; } = new List<HmiText>();

    public List<HmiText>? EventText9 { get; set; } = new List<HmiText>();

    /// <summary>The parameter-tag names in order (10 observed, <c>""</c> when unset); null when the value is not a list of strings.</summary>
    public List<string>? AlarmParameterTags { get; set; } = new List<string>();
}

/// <summary>Result of <c>list_alarm_classes</c>: three arrays ordered by name.</summary>
public class HmiAlarmClassesInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public List<HmiAlarmClassInfo> AlarmClasses { get; set; } = new List<HmiAlarmClassInfo>();

    /// <summary>Audit class names only: the audit/GMP properties were never read live (see the spike report).</summary>
    public List<HmiAuditClassInfo> AuditClasses { get; set; } = new List<HmiAuditClassInfo>();

    public List<HmiOpcUaAlarmTypeInfo> OpcUaAlarmTypes { get; set; } = new List<HmiOpcUaAlarmTypeInfo>();
}

public class HmiAlarmClassInfo
{
    public string Name { get; set; } = string.Empty;

    public long? Id { get; set; }

    public int? Priority { get; set; }

    public bool? IsSystem { get; set; }

    public string? Log { get; set; }

    public string? StateMachine { get; set; }

    public string? CommonAlarmClass { get; set; }
}

public class HmiAuditClassInfo
{
    public string Name { get; set; } = string.Empty;
}

public class HmiOpcUaAlarmTypeInfo
{
    public string Name { get; set; } = string.Empty;

    public string? AlarmClass { get; set; }

    public string? Area { get; set; }

    public string? ConditionTypeId { get; set; }

    public string? Connection { get; set; }

    public string? NodeId { get; set; }
}
