namespace TiaMcpServer.Contracts.Hmi;

/// <summary>Result of <c>list_tag_tables</c>: the tables and groups below the requested group (the root when none), with each group's tables and subgroups nested recursively.</summary>
public class HmiTagTableTreeInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    /// <summary>The requested group path; null at the root.</summary>
    public string? GroupPath { get; set; }

    public List<HmiTagTableInfo> Tables { get; set; } = new List<HmiTagTableInfo>();

    public List<HmiTagTableGroupInfo> Groups { get; set; } = new List<HmiTagTableGroupInfo>();
}

public class HmiTagTableInfo
{
    public string Name { get; set; } = string.Empty;

    public int TagCount { get; set; }
}

public class HmiTagTableGroupInfo
{
    public string Name { get; set; } = string.Empty;

    public List<HmiTagTableInfo> Tables { get; set; } = new List<HmiTagTableInfo>();

    public List<HmiTagTableGroupInfo> Groups { get; set; } = new List<HmiTagTableGroupInfo>();
}

/// <summary>Result of <c>list_tags</c>: one page of tag rows ordered by name. Enums are their names.</summary>
public class HmiTagListInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public HmiPage Page { get; set; } = new HmiPage(0, 0, 0, null);

    public List<HmiTagRowInfo> Tags { get; set; } = new List<HmiTagRowInfo>();
}

/// <summary>The core row of a tag; an unreadable property is null (see the result's messages).</summary>
public class HmiTagRowInfo
{
    public string Name { get; set; } = string.Empty;

    public string? Table { get; set; }

    public string? Connection { get; set; }

    public string? PlcTag { get; set; }

    public string? DataType { get; set; }

    public string? HmiDataType { get; set; }

    public string? Address { get; set; }

    public string? AcquisitionMode { get; set; }

    public string? AcquisitionCycle { get; set; }

    public string? AccessMode { get; set; }

    public bool? Persistent { get; set; }

    public bool? LinearScaling { get; set; }

    /// <summary>Null when the comment could not be read; empty when it has no text in the requested language.</summary>
    public List<HmiText>? Comment { get; set; } = new List<HmiText>();
}

/// <summary>Result of <c>get_tag</c>: the core row plus members, thresholds, logging tags, ranges, substitute value and variants.</summary>
public class HmiTagDetailInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public HmiTagRowInfo Tag { get; set; } = new HmiTagRowInfo();

    public List<HmiTagMemberInfo> Members { get; set; } = new List<HmiTagMemberInfo>();

    /// <summary>
    /// True when members were not read: a member at the depth cap (8) has members, or the row cap (100 member rows,
    /// all levels combined) was hit, which also adds one message. Neither cap makes the result incomplete.
    /// </summary>
    public bool MembersTruncated { get; set; }

    public List<HmiThresholdInfo> Thresholds { get; set; } = new List<HmiThresholdInfo>();

    public List<HmiLoggingTagRowInfo> LoggingTags { get; set; } = new List<HmiLoggingTagRowInfo>();

    public HmiRangeInfo? UpperRange { get; set; }

    public HmiRangeInfo? LowerRange { get; set; }

    public HmiSubstituteValueInfo? SubstituteValue { get; set; }

    public HmiVariant? InitialValue { get; set; }

    public HmiVariant? HmiStartValue { get; set; }

    public HmiVariant? HmiEndValue { get; set; }

    public HmiVariant? PlcStartValue { get; set; }

    public HmiVariant? PlcEndValue { get; set; }
}

/// <summary>A tag member (core row) and its own members, recursively.</summary>
public class HmiTagMemberInfo
{
    public HmiTagRowInfo Tag { get; set; } = new HmiTagRowInfo();

    public List<HmiTagMemberInfo> Members { get; set; } = new List<HmiTagMemberInfo>();
}

public class HmiThresholdInfo
{
    public string? Name { get; set; }

    public string? Mode { get; set; }

    public HmiVariant? Value { get; set; }

    public string? ValueType { get; set; }
}

public class HmiRangeInfo
{
    public HmiVariant? Value { get; set; }

    public string? ValueType { get; set; }
}

public class HmiSubstituteValueInfo
{
    public string? Usage { get; set; }

    public HmiVariant? Value { get; set; }
}

/// <summary>Result of <c>list_system_tags</c>: one page ordered by name.</summary>
public class HmiSystemTagListInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public HmiPage Page { get; set; } = new HmiPage(0, 0, 0, null);

    public List<HmiSystemTagInfo> SystemTags { get; set; } = new List<HmiSystemTagInfo>();
}

public class HmiSystemTagInfo
{
    public string Name { get; set; } = string.Empty;

    public string? DataType { get; set; }
}

/// <summary>Result of <c>list_logging_tags</c>: one page ordered by logging tag name, then owning tag.</summary>
public class HmiLoggingTagListInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public HmiPage Page { get; set; } = new HmiPage(0, 0, 0, null);

    public List<HmiLoggingTagRowInfo> LoggingTags { get; set; } = new List<HmiLoggingTagRowInfo>();
}

public class HmiLoggingTagRowInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The tag that owns the logging tag.</summary>
    public string Tag { get; set; } = string.Empty;

    public string? DataLog { get; set; }

    public string? LoggingMode { get; set; }

    public string? Cycle { get; set; }

    public string? TriggerMode { get; set; }

    public HmiVariant? HighLimit { get; set; }

    public HmiVariant? LowLimit { get; set; }
}
