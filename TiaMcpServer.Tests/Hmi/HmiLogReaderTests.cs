using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiLogging;
using Siemens.Engineering.HmiUnified.HmiLogging.HmiLoggingCommon;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using Xunit;
using static TiaMcpServer.Tests.Hmi.HmiTestProjects;

namespace TiaMcpServer.Tests.Hmi;

public class HmiLogReaderTests
{
    private static readonly DateTime Start = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    private static LogSettings Settings(string folder) => new()
    {
        LogMaxSize = 500,
        LogTimePeriod = new LogDuration { Days = 7, Hours = 1, Minutes = 2, Seconds = 3 },
        StorageDevice = DeviceNode.Local,
        StorageFolder = folder,
    };

    private static LogSegment Segment() => new()
    {
        SegmentMaxSize = 100,
        SegmentStartTime = Start,
        SegmentTimePeriod = new SegmentDuration { Days = 1, Hours = 0, Minutes = 30, Seconds = 0 },
    };

    private static LogBackup Backup() => new() { BackupMode = HmiBackupMode.PrimaryPath, PrimaryPath = @"D:\backup" };

    private static T Fill<T>(T log, string folder) where T : LoggingBase
    {
        log.Settings = Settings(folder);
        log.Segment = Segment();
        log.Backup = Backup();
        return log;
    }

    [Fact]
    public void DataAlarmAndAuditLogsWithSettings()
    {
        var software = Unified("Panel_RT");
        software.DataLogs.Items.Add(Fill(new HmiDataLog { Name = "b_data" }, "b"));
        software.DataLogs.Items.Add(Fill(new HmiDataLog { Name = "A_data" }, "a"));
        software.AlarmLogs.Items.Add(Fill(new HmiAlarmLog { Name = "Alarms" }, "al"));
        software.AuditTrails.Items.Add(Fill(new HmiAuditTrail { Name = "Audit" }, "au"));

        var info = HmiLogReader.ListLogs(software);

        Assert.True(info.IsComplete);
        Assert.Empty(info.Messages);
        Assert.Equal(new[] { "A_data", "b_data" }, info.DataLogs.Select(l => l.Name));
        Assert.Equal("Alarms", Assert.Single(info.AlarmLogs).Name);
        Assert.Equal("Audit", Assert.Single(info.AuditTrails).Name);

        var log = info.DataLogs[0];
        Assert.Equal(500, log.Settings!.LogMaxSize);
        Assert.Equal("Local", log.Settings.StorageDevice);
        Assert.Equal("a", log.Settings.StorageFolder);
        Assert.Equal((7, 1, 2, 3), (log.Settings.LogTimePeriod!.Days, log.Settings.LogTimePeriod.Hours, log.Settings.LogTimePeriod.Minutes, log.Settings.LogTimePeriod.Seconds));
        Assert.Equal(100, log.Segment!.SegmentMaxSize);
        Assert.Equal(Start.ToString("o", System.Globalization.CultureInfo.InvariantCulture), log.Segment.SegmentStartTime);
        Assert.Equal((1, 0, 30, 0), (log.Segment.SegmentTimePeriod!.Days, log.Segment.SegmentTimePeriod.Hours, log.Segment.SegmentTimePeriod.Minutes, log.Segment.SegmentTimePeriod.Seconds));
        Assert.Equal("PrimaryPath", log.Backup!.BackupMode);
        Assert.Equal(@"D:\backup", log.Backup.PrimaryPath);
    }

    [Fact]
    public void UnreadablePropertyIsNullWithAMessageAndAnIncompleteResult()
    {
        var software = Unified("Panel_RT");
        var data = Fill(new HmiDataLog { Name = "Data" }, "f");
        data.Settings.Failures["StorageFolder"] = new EngineeringTargetInvocationException("not supported");
        data.Backup.Failures["BackupMode"] = new EngineeringNotSupportedException("no backup here");
        software.DataLogs.Items.Add(data);

        var info = HmiLogReader.ListLogs(software);

        var log = Assert.Single(info.DataLogs);
        Assert.Null(log.Settings!.StorageFolder);
        Assert.Equal("Local", log.Settings.StorageDevice);
        Assert.Null(log.Backup!.BackupMode);
        Assert.Equal(@"D:\backup", log.Backup.PrimaryPath);
        Assert.False(info.IsComplete);
        Assert.Contains(info.Messages, m => m.Contains("StorageFolder") && m.Contains("Data"));
        Assert.Contains(info.Messages, m => m.Contains("BackupMode") && m.Contains("Data"));
    }

    [Fact]
    public void UnreadableSubObjectIsNullAndAUnreadableCompositionFailsTheItem()
    {
        var software = Unified("Panel_RT");
        var data = Fill(new HmiDataLog { Name = "Data" }, "f");
        data.Failures["Segment"] = new EngineeringTargetInvocationException("no segment");
        software.DataLogs.Items.Add(data);

        var info = HmiLogReader.ListLogs(software);
        Assert.Null(Assert.Single(info.DataLogs).Segment);
        Assert.False(info.IsComplete);

        software.AlarmLogs.EnumerationFailure = new EngineeringTargetInvocationException("gone");
        var ex = Assert.Throws<WorkerOperationException>(() => HmiLogReader.ListLogs(software));
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
    }

    [Fact]
    public void RequireDataLogAcceptsAKnownNameAndRejectsAnUnknownOne()
    {
        var software = Unified("Panel_RT");
        software.DataLogs.Items.Add(new HmiDataLog { Name = "Data" });

        HmiLogReader.RequireDataLog(software, "data");
        var ex = Assert.Throws<WorkerOperationException>(() => HmiLogReader.RequireDataLog(software, "Nope"));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, ex.FailureCategory);
        Assert.Contains("list_logs", ex.Message);
    }
}
