namespace TiaMcpServer.Contracts;

/// <summary>Result of <c>list_logs</c>: data logs, alarm logs and audit trails, each ordered by name.</summary>
public class HmiLogsInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public List<HmiLogInfo> DataLogs { get; set; } = new List<HmiLogInfo>();

    public List<HmiLogInfo> AlarmLogs { get; set; } = new List<HmiLogInfo>();

    public List<HmiLogInfo> AuditTrails { get; set; } = new List<HmiLogInfo>();
}

/// <summary>One log. A sub-object or member that could not be read is null.</summary>
public class HmiLogInfo
{
    public string Name { get; set; } = string.Empty;

    public HmiLogSettingsInfo? Settings { get; set; }

    public HmiLogSegmentInfo? Segment { get; set; }

    public HmiLogBackupInfo? Backup { get; set; }
}

public class HmiLogSettingsInfo
{
    public long? LogMaxSize { get; set; }

    public HmiDurationInfo? LogTimePeriod { get; set; }

    public string? StorageDevice { get; set; }

    public string? StorageFolder { get; set; }
}

public class HmiLogSegmentInfo
{
    public long? SegmentMaxSize { get; set; }

    /// <summary>Round-trip ISO 8601 text of the segment start time.</summary>
    public string? SegmentStartTime { get; set; }

    public HmiDurationInfo? SegmentTimePeriod { get; set; }
}

public class HmiLogBackupInfo
{
    public string? BackupMode { get; set; }

    public string? PrimaryPath { get; set; }
}

public class HmiDurationInfo
{
    public long? Days { get; set; }

    public long? Hours { get; set; }

    public long? Minutes { get; set; }

    public long? Seconds { get; set; }
}
