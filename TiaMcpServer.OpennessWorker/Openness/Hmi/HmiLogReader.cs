using System.Globalization;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiLogging.HmiLoggingCommon;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>
/// Operation 10: data logs, alarm logs and audit trails with settings, segment and backup. Reads enumerate the
/// compositions and read properties from explicit lists; an unreadable composition fails the item, an unreadable
/// sub-object or member is null plus a message.
/// </summary>
public static class HmiLogReader
{
    public static HmiLogsInfo ListLogs(HmiSoftware software)
    {
        var log = new HmiReadLog();
        var dataLogs = HmiReadLog.Guard(() => software.DataLogs.ToList(), "The data logs");
        var alarmLogs = HmiReadLog.Guard(() => software.AlarmLogs.ToList(), "The alarm logs");
        var auditTrails = HmiReadLog.Guard(() => software.AuditTrails.ToList(), "The audit trails");
        return new HmiLogsInfo
        {
            DataLogs = Read(dataLogs, l => l.Name, "data log", log),
            AlarmLogs = Read(alarmLogs, l => l.Name, "alarm log", log),
            AuditTrails = Read(auditTrails, l => l.Name, "audit trail", log),
            IsComplete = log.IsComplete,
            Messages = log.Messages,
        };
    }

    /// <summary>Fails the item <c>target_not_found</c> when the HMI has no data log of that name.</summary>
    public static void RequireDataLog(HmiSoftware software, string dataLogName)
    {
        var exists = HmiReadLog.Guard(() => software.DataLogs.ToList(), "The data logs")
            .Any(l => string.Equals(HmiReadLog.Guard(() => l.Name, "A data log name"), dataLogName, StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.TargetNotFound,
                $"Data log '{dataLogName}' was not found. Use list_logs to see the data logs.");
        }
    }

    /// <summary>Every data log, alarm log and audit trail with its name, for <c>validate</c>.</summary>
    internal static List<(object Item, string Name)> NamedLogs(HmiSoftware software)
    {
        var all = new List<(object, string)>();
        all.AddRange(HmiReadLog.Guard(() => software.DataLogs.ToList(), "The data logs")
            .Select(l => ((object)l, HmiReadLog.Guard(() => l.Name, "A data log name"))));
        all.AddRange(HmiReadLog.Guard(() => software.AlarmLogs.ToList(), "The alarm logs")
            .Select(l => ((object)l, HmiReadLog.Guard(() => l.Name, "An alarm log name"))));
        all.AddRange(HmiReadLog.Guard(() => software.AuditTrails.ToList(), "The audit trails")
            .Select(l => ((object)l, HmiReadLog.Guard(() => l.Name, "An audit trail name"))));
        return all;
    }

    private static List<HmiLogInfo> Read<T>(IEnumerable<T> logs, Func<T, string> nameOf, string kind, HmiReadLog log)
        where T : LoggingBase
    {
        var infos = logs
            .Select(l => ReadLog(l, HmiReadLog.Guard(() => nameOf(l), $"A {kind} name"), kind, log))
            .ToList();
        return HmiPager.InNameOrder(infos, i => i.Name).ToList();
    }

    private static HmiLogInfo ReadLog(LoggingBase source, string name, string kind, HmiReadLog log)
    {
        string What(string property) => $"Property {property} of {kind} '{name}'";
        var settings = log.Try(() => source.Settings, What("Settings"));
        var segment = log.Try(() => source.Segment, What("Segment"));
        var backup = log.Try(() => source.Backup, What("Backup"));
        return new HmiLogInfo
        {
            Name = name,
            Settings = settings is null ? null : new HmiLogSettingsInfo
            {
                LogMaxSize = log.Try(() => (long?)settings.LogMaxSize, What("Settings.LogMaxSize")),
                LogTimePeriod = Duration(log, What("Settings.LogTimePeriod"), () => settings.LogTimePeriod, d => d.Days, d => d.Hours, d => d.Minutes, d => d.Seconds),
                StorageDevice = log.Try(() => settings.StorageDevice.ToString(), What("Settings.StorageDevice")),
                StorageFolder = log.Try(() => settings.StorageFolder, What("Settings.StorageFolder")),
            },
            Segment = segment is null ? null : new HmiLogSegmentInfo
            {
                SegmentMaxSize = log.Try(() => (long?)segment.SegmentMaxSize, What("Segment.SegmentMaxSize")),
                SegmentStartTime = log.Try(() => segment.SegmentStartTime.ToString("o", CultureInfo.InvariantCulture), What("Segment.SegmentStartTime")),
                SegmentTimePeriod = Duration(log, What("Segment.SegmentTimePeriod"), () => segment.SegmentTimePeriod, d => d.Days, d => d.Hours, d => d.Minutes, d => d.Seconds),
            },
            Backup = backup is null ? null : new HmiLogBackupInfo
            {
                BackupMode = log.Try(() => backup.BackupMode.ToString(), What("Backup.BackupMode")),
                PrimaryPath = log.Try(() => backup.PrimaryPath, What("Backup.PrimaryPath")),
            },
        };
    }

    /// <summary>LogDuration and SegmentDuration are distinct Openness types with the same four members.</summary>
    private static HmiDurationInfo? Duration<T>(
        HmiReadLog log, string what, Func<T?> read,
        Func<T, uint> days, Func<T, uint> hours, Func<T, uint> minutes, Func<T, uint> seconds)
        where T : class
    {
        var duration = log.Try(read, what);
        if (duration is null)
        {
            return null;
        }

        return new HmiDurationInfo
        {
            Days = log.Try(() => (long?)days(duration), what + ".Days"),
            Hours = log.Try(() => (long?)hours(duration), what + ".Hours"),
            Minutes = log.Try(() => (long?)minutes(duration), what + ".Minutes"),
            Seconds = log.Try(() => (long?)seconds(duration), what + ".Seconds"),
        };
    }
}
