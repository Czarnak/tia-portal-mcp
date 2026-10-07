using Siemens.Engineering;
using Siemens.Engineering.HmiUnified.Common;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using Siemens.Engineering.HmiUnified.HmiConnections;
using Siemens.Engineering.HmiUnified.HmiLogging;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.LoggingTags;
using Siemens.Engineering.HmiUnified.UI.Screens;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using Xunit;
using static TiaMcpServer.Tests.Hmi.HmiTestProjects;

namespace TiaMcpServer.Tests.Hmi;

public class HmiValidationReaderTests
{
    private static HmiValidationResult Finding(string property, string? error = null, string? warning = null) => new()
    {
        PropertyName = property,
        Errors = error is null ? Array.Empty<string>() : new[] { error },
        Warnings = warning is null ? Array.Empty<string>() : new[] { warning },
    };

    private static HmiTag Tag(string name) => new() { Name = name };

    [Fact]
    public void PagesOverScannedObjectsNotFindings()
    {
        var software = Unified("Panel_RT");
        var tags = new[] { Tag("t1"), Tag("t2"), Tag("t3"), Tag("t4"), Tag("t5") };
        tags[2].Results.Add(Finding("Address", error: "bad address"));
        tags[4].Results.Add(Finding("DataType", warning: "odd type"));
        software.Tags.Items.AddRange(tags.Reverse());

        var first = HmiValidationReader.Validate(software, "tags", null, 0, 2);

        Assert.Equal(new HmiPage(0, 2, 5, 2), first.Page);
        Assert.Equal(2, first.Scanned);
        Assert.Equal(2, first.Clean);
        Assert.Empty(first.Findings);
        Assert.Equal(new[] { 1, 1, 0, 0, 0 }, tags.Select(t => t.ValidateCalls));

        var second = HmiValidationReader.Validate(software, "tags", null, 2, 2);

        Assert.Equal(2, second.Scanned);
        Assert.Equal(1, second.Clean);
        Assert.Equal("t3", Assert.Single(second.Findings).Name);

        var last = HmiValidationReader.Validate(software, "tags", null, 4, 2);

        Assert.Equal(new HmiPage(4, 2, 5, null), last.Page);
        Assert.Equal(1, last.Scanned);
        Assert.Equal(0, last.Clean);
        Assert.Equal("t5", Assert.Single(last.Findings).Name);
    }

    [Fact]
    public void CleanObjectsCountedNotListed()
    {
        var software = Unified("Panel_RT");
        var bad = new HmiConnection { Name = "PLC_2" };
        bad.Results.Add(Finding("InitialAddress", error: "empty", warning: "unusual"));
        software.Connections.Items.AddRange(new[] { new HmiConnection { Name = "PLC_1" }, bad, new HmiConnection { Name = "PLC_3" } });

        var info = HmiValidationReader.Validate(software, "connections", null, 0, 100);

        Assert.True(info.IsComplete);
        Assert.Empty(info.Messages);
        Assert.Equal(3, info.Scanned);
        Assert.Equal(2, info.Clean);
        var finding = Assert.Single(info.Findings);
        Assert.Equal(("HmiConnection", "PLC_2"), (finding.ObjectKind, finding.Name));
        var result = Assert.Single(finding.Results);
        Assert.Equal("InitialAddress", result.PropertyName);
        Assert.Equal(new[] { "empty" }, result.Errors);
        Assert.Equal(new[] { "unusual" }, result.Warnings);
        Assert.Empty(info.NotValidatable);
    }

    [Fact]
    public void NameNarrowsToOneObjectAndMissingIsTargetNotFound()
    {
        var software = Unified("Panel_RT");
        software.Screens.Items.AddRange(new[] { new HmiScreen { Name = "Start" }, new HmiScreen { Name = "Other" } });

        var info = HmiValidationReader.Validate(software, "screens", "start", 0, 100);

        Assert.Equal(1, info.Scanned);
        Assert.Equal(1, info.Page.Total);
        Assert.Equal(1, info.Clean);

        var ex = Assert.Throws<WorkerOperationException>(() => HmiValidationReader.Validate(software, "screens", "Nope", 0, 100));
        Assert.Equal(WorkerFailureCategories.TargetNotFound, ex.FailureCategory);
    }

    [Fact]
    public void AllCategoryOrdersByKindThenName()
    {
        var software = Unified("Panel_RT");
        software.Connections.Items.Add(Failing(new HmiConnection { Name = "z" }));
        software.DataLogs.Items.Add(Failing(new HmiDataLog { Name = "a" }));
        software.DiscreteAlarms.Items.Add(Failing(new HmiDiscreteAlarm { Name = "m" }));
        software.AlarmClasses.Items.Add(Failing(new HmiAlarmClass { Name = "k" }));
        software.Screens.Items.Add(Failing(new HmiScreen { Name = "b" }));
        software.Tags.Items.AddRange(new[] { Failing(Tag("c")), Failing(Tag("A")) });

        var info = HmiValidationReader.Validate(software, "all", null, 0, 100);

        Assert.Equal(7, info.Scanned);
        Assert.Equal(
            new[] { ("HmiAlarmClass", "k"), ("HmiConnection", "z"), ("HmiDataLog", "a"), ("HmiDiscreteAlarm", "m"), ("HmiScreen", "b"), ("HmiTag", "A"), ("HmiTag", "c") },
            info.Findings.Select(f => (f.ObjectKind, f.Name)));

        static T Failing<T>(T item) where T : ValidatableBag
        {
            item.Results.Add(Finding("P", error: "e"));
            return item;
        }
    }

    [Fact]
    public void NonValidatableKindsListedOnce()
    {
        var software = Unified("Panel_RT");
        software.Tags.Items.Add(Tag("t"));
        software.SystemTags.Items.AddRange(new[] { new HmiSystemTag { Name = "@a" }, new HmiSystemTag { Name = "@b" } });

        var info = HmiValidationReader.Validate(software, "tags", null, 0, 100);

        Assert.Equal(3, info.Scanned);
        Assert.Equal(1, info.Clean);
        Assert.Empty(info.Findings);
        Assert.Equal(new[] { "HmiSystemTag" }, info.NotValidatable);
        Assert.True(info.IsComplete);
    }

    [Fact]
    public void ValidateThrowingMarksObjectIncompleteWithMessage()
    {
        var software = Unified("Panel_RT");
        var broken = new HmiDiscreteAlarm { Name = "Broken", ValidateFailure = new EngineeringTargetInvocationException("not supported here") };
        software.DiscreteAlarms.Items.AddRange(new[] { new HmiDiscreteAlarm { Name = "Fine" }, broken });

        var info = HmiValidationReader.Validate(software, "alarms", null, 0, 100);

        Assert.False(info.IsComplete);
        Assert.Equal(2, info.Scanned);
        Assert.Equal(1, info.Clean);
        Assert.Empty(info.Findings);
        var message = Assert.Single(info.Messages);
        Assert.Contains("Broken", message);
        Assert.Contains("not supported here", message);
    }

    [Fact]
    public void UnreadableResultErrorsOrWarningsAreNeverCountedClean()
    {
        var software = Unified("Panel_RT");
        var tag = Tag("t");
        tag.Results.Add(new HmiValidationResult
        {
            PropertyName = "Address",
            ErrorsFailure = new EngineeringTargetInvocationException("errors gone"),
            WarningsFailure = new EngineeringTargetInvocationException("warnings gone"),
        });
        software.Tags.Items.AddRange(new[] { tag, Tag("u") });

        var info = HmiValidationReader.Validate(software, "tags", null, 0, 100);

        Assert.Equal(2, info.Scanned);
        Assert.Equal(1, info.Clean);
        Assert.Empty(info.Findings);
        Assert.False(info.IsComplete);
        Assert.Contains("errors gone", Assert.Single(info.Messages));
    }

    [Fact]
    public void NullValidateResultOrNullErrorsAreUnvalidatedNotClean()
    {
        var software = Unified("Panel_RT");
        var nullList = new HmiConnection { Name = "a", ValidateReturnsNull = true };
        var nullErrors = new HmiConnection { Name = "b" };
        nullErrors.Results.Add(new HmiValidationResult { PropertyName = "P", Errors = null });
        software.Connections.Items.AddRange(new[] { nullList, nullErrors, new HmiConnection { Name = "c" } });

        var info = HmiValidationReader.Validate(software, "connections", null, 0, 100);

        Assert.Equal(3, info.Scanned);
        Assert.Equal(1, info.Clean);
        Assert.Empty(info.Findings);
        Assert.False(info.IsComplete);
        Assert.Equal(2, info.Messages.Count);
    }

    [Fact]
    public void ANonRecoverableValidateFailureFailsTheItem()
    {
        var software = Unified("Panel_RT");
        software.Tags.Items.Add(new HmiTag { Name = "t", ValidateFailure = new EngineeringObjectDisposedException("gone") });

        var ex = Assert.Throws<WorkerOperationException>(() => HmiValidationReader.Validate(software, "tags", null, 0, 100));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
    }

    [Fact]
    public void AnUnreadableCompositionFailsTheItem()
    {
        var software = Unified("Panel_RT");
        software.Connections.EnumerationFailure = new EngineeringTargetInvocationException("gone");

        var ex = Assert.Throws<WorkerOperationException>(() => HmiValidationReader.Validate(software, "connections", null, 0, 100));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
    }

    [Fact]
    public void LogsAndAlarmClassesCategoriesCoverEveryKind()
    {
        var software = Unified("Panel_RT");
        software.DataLogs.Items.Add(new HmiDataLog { Name = "d" });
        software.AlarmLogs.Items.Add(new HmiAlarmLog { Name = "a" });
        software.AuditTrails.Items.Add(new HmiAuditTrail { Name = "t" });
        software.AlarmClasses.Items.Add(new HmiAlarmClass { Name = "c" });
        software.HmiAlarmAuditClass.Items.Add(new Siemens.Engineering.HmiUnified.HmiAudit.HmiAlarmAuditClass { Name = "u" });
        software.OpcUaAlarmTypes.Items.Add(new Siemens.Engineering.HmiUnified.HmiOpcUaAlarm.HmiOpcUaAlarmType { Name = "o" });

        Assert.Equal(3, HmiValidationReader.Validate(software, "logs", null, 0, 100).Scanned);
        Assert.Equal(3, HmiValidationReader.Validate(software, "alarmClasses", null, 0, 100).Scanned);
    }

    [Fact]
    public void DispatchRoutesValidateAndRequiresTheCategory()
    {
        var software = Unified("Panel_RT");
        software.Tags.Items.Add(Tag("t"));
        var project = ProjectWith(DeviceWith("Panel", software));

        var info = Assert.IsType<HmiValidationInfo>(
            HmiReadDispatch.Read(project, "hmi_validate", new HmiQueryInfo { Category = "tags", Offset = 0, Limit = 10 }));
        var ex = Assert.Throws<WorkerOperationException>(() => HmiReadDispatch.Read(project, "hmi_validate", new HmiQueryInfo()));

        Assert.Equal(1, info.Scanned);
        Assert.Equal(WorkerFailureCategories.ValidationError, ex.FailureCategory);
    }
}
