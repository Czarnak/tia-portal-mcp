using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon;
using Siemens.Engineering.HmiUnified.HmiAudit;
using Siemens.Engineering.HmiUnified.HmiOpcUaAlarm;
using Siemens.Engineering.HmiUnified.HmiTags;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using Xunit;
using static TiaMcpServer.Tests.Hmi.HmiTestProjects;

namespace TiaMcpServer.Tests.Hmi;

public class HmiAlarmReaderTests
{
    private const string NoTag = "<No tag>";

    private static MultilingualText Texts(string en, string pl)
        => new MultilingualText().With("en-US", en).With("pl-PL", pl);

    private static HmiDiscreteAlarm Discrete(string name) => new()
    {
        Name = name,
        AlarmClass = "Errors",
        Priority = 3,
        RaisedStateTag = "Tag_" + name,
        RaisedStateTagBitNumber = 7,
        TriggerMode = HmiDiscreteAlarmTriggerMode.OnRisingEdge,
        AcknowledgmentControlTag = NoTag,
        AcknowledgmentControlTagBitNumber = 0,
        AcknowledgmentStateTag = "AckState",
        AcknowledgmentStateTagBitNumber = 2,
        EventText = Texts("<body><p>raised " + name + "</p></body>", "<body><p>podniesiony</p></body>"),
        InfoText = Texts(string.Empty, string.Empty),
        AlarmParameterTags = new List<string> { "p1", "", "", "", "", "", "", "", "", "p10" },
    };

    private static HmiAnalogAlarm Analog(string name) => new()
    {
        Name = name,
        AlarmClass = "Warnings",
        Priority = 9,
        RaisedStateTag = "Level",
        Condition = HmiAlarmCondition.UpperLimit,
        ConditionValue = 80.5d,
        EventText = Texts("<body><p>high</p></body>", "<body><p>wysoki</p></body>"),
        InfoText = Texts("<body><p>info</p></body>", string.Empty),
        AlarmParameterTags = new List<string>(),
    };

    private static HmiSoftware SoftwareWith(IEnumerable<HmiDiscreteAlarm> discrete, IEnumerable<HmiAnalogAlarm> analog)
    {
        var software = Unified("Panel_RT");
        software.DiscreteAlarms.Items.AddRange(discrete);
        software.AnalogAlarms.Items.AddRange(analog);
        return software;
    }

    private static HmiSoftware Mixed() => SoftwareWith(
        new[] { Discrete("b_disc"), Discrete("A_disc") },
        new[] { Analog("c_an"), Analog("a_an") });

    [Fact]
    public void ListMergesDiscreteAndAnalogSortedByName()
    {
        var first = HmiAlarmReader.ListAlarms(Mixed(), null, null, 0, 3);
        var second = HmiAlarmReader.ListAlarms(Mixed(), null, null, 3, 3);

        Assert.True(first.IsComplete);
        Assert.Equal(new[] { "a_an", "A_disc", "b_disc" }, first.Alarms.Select(a => a.Name));
        Assert.Equal(new[] { "analog", "discrete", "discrete" }, first.Alarms.Select(a => a.Kind));
        Assert.Equal(new HmiPage(0, 3, 4, 3), first.Page);
        Assert.Equal(new[] { "c_an" }, second.Alarms.Select(a => a.Name));
        Assert.Null(second.Page.NextOffset);

        var discrete = first.Alarms[1];
        Assert.Equal("Errors", discrete.AlarmClass);
        Assert.Equal(3, discrete.Priority);
        Assert.Equal("Tag_A_disc", discrete.TriggerTag);
        Assert.Equal(7, discrete.TriggerBit);
        Assert.Equal("OnRisingEdge", discrete.TriggerMode);
        Assert.Equal(NoTag, discrete.AcknowledgmentControlTag);
        Assert.Equal(0, discrete.AcknowledgmentControlTagBit);
        Assert.Equal("AckState", discrete.AcknowledgmentStateTag);
        Assert.Equal(2, discrete.AcknowledgmentStateTagBit);
        Assert.Null(discrete.Condition);
        Assert.Null(discrete.ConditionValue);

        var analog = first.Alarms[0];
        Assert.Equal("UpperLimit", analog.Condition);
        Assert.Equal("System.Double", analog.ConditionValue!.Type);
        Assert.Equal(80.5d, analog.ConditionValue.Value!.Value.GetDouble());
        Assert.Equal("Level", analog.TriggerTag);
        Assert.Null(analog.TriggerBit);
        Assert.Null(analog.TriggerMode);
        Assert.Null(analog.AcknowledgmentControlTag);
        Assert.Null(analog.AcknowledgmentStateTagBit);
    }

    [Fact]
    public void AlarmKindNarrows()
    {
        var analogOnly = HmiAlarmReader.ListAlarms(Mixed(), "analog", null, 0, 10);
        var discreteOnly = HmiAlarmReader.ListAlarms(Mixed(), "discrete", null, 0, 10);

        Assert.Equal(new[] { "a_an", "c_an" }, analogOnly.Alarms.Select(a => a.Name));
        Assert.Equal(2, analogOnly.Page.Total);
        Assert.All(analogOnly.Alarms, a => Assert.Equal("analog", a.Kind));
        Assert.Equal(new[] { "A_disc", "b_disc" }, discreteOnly.Alarms.Select(a => a.Name));
        Assert.All(discreteOnly.Alarms, a => Assert.Equal("discrete", a.Kind));
    }

    [Fact]
    public void ListCarriesOnlyEventAndInfoText()
    {
        var software = SoftwareWith(new[] { Discrete("d") }, new[] { Analog("a") });

        var info = HmiAlarmReader.ListAlarms(software, null, null, 0, 10);

        var discrete = info.Alarms.Single(a => a.Name == "d");
        Assert.Equal(new[] { "en-US", "pl-PL" }, discrete.EventText!.Select(t => t.Culture));
        Assert.Equal("<body><p>raised d</p></body>", discrete.EventText![0].Text);
        Assert.Equal(string.Empty, discrete.InfoText![0].Text);
        var analog = info.Alarms.Single(a => a.Name == "a");
        Assert.Equal("<body><p>info</p></body>", analog.InfoText![0].Text);

        foreach (PropertyBagNamed alarm in new PropertyBagNamed[] { software.DiscreteAlarms.Items[0], software.AnalogAlarms.Items[0] })
        {
            Assert.DoesNotContain(alarm.Reads, r => r.StartsWith("EventText") && r != "EventText");
            Assert.DoesNotContain("AlarmParameterTags", alarm.Reads);
            Assert.Equal(1, alarm.Reads.Count(r => r == "Priority"));
            Assert.Equal(1, alarm.Reads.Count(r => r == "EventText"));
        }
    }

    [Fact]
    public void LanguageNarrowsTheListTexts()
    {
        var info = HmiAlarmReader.ListAlarms(SoftwareWith(new[] { Discrete("d") }, Array.Empty<HmiAnalogAlarm>()), null, "pl-PL", 0, 10);

        var alarm = Assert.Single(info.Alarms);
        Assert.Equal("<body><p>podniesiony</p></body>", Assert.Single(alarm.EventText!).Text);
    }

    [Fact]
    public void UnreadableAlarmPropertyIsNullWithMessageAndIncomplete()
    {
        var bad = Discrete("bad");
        bad.Failures["AcknowledgmentStateTag"] = new EngineeringTargetInvocationException("no ack state");
        bad.Failures["EventText"] = new EngineeringNotSupportedException("no event text");

        var info = HmiAlarmReader.ListAlarms(SoftwareWith(new[] { bad }, Array.Empty<HmiAnalogAlarm>()), null, null, 0, 10);

        var row = Assert.Single(info.Alarms);
        Assert.Null(row.AcknowledgmentStateTag);
        Assert.Null(row.EventText);
        Assert.Equal("Errors", row.AlarmClass);
        Assert.False(info.IsComplete);
        Assert.Equal(2, info.Messages.Count);
        Assert.Contains(info.Messages, m => m.Contains("AcknowledgmentStateTag") && m.Contains("'bad'"));
    }

    [Fact]
    public void NonRecoverableAlarmPropertyFailureFailsTheItem()
    {
        var bad = Discrete("bad");
        bad.Failures["Priority"] = new EngineeringObjectDisposedException("TIA Portal has been disposed.");

        var ex = Assert.Throws<WorkerOperationException>(
            () => HmiAlarmReader.ListAlarms(SoftwareWith(new[] { bad }, Array.Empty<HmiAnalogAlarm>()), null, null, 0, 10));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
    }

    [Fact]
    public void UnreadableAlarmCompositionFailsTheItem()
    {
        var software = Mixed();
        software.AnalogAlarms.EnumerationFailure = new EngineeringTargetInvocationException("analog alarms unavailable");

        var ex = Assert.Throws<WorkerOperationException>(() => HmiAlarmReader.ListAlarms(software, null, null, 0, 10));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
    }

    [Fact]
    public void KindFilterDoesNotTouchTheOtherComposition()
    {
        var software = Mixed();
        software.AnalogAlarms.EnumerationFailure = new EngineeringTargetInvocationException("analog alarms unavailable");

        var info = HmiAlarmReader.ListAlarms(software, "discrete", null, 0, 10);

        Assert.Equal(2, info.Alarms.Count);
    }

    [Fact]
    public void DetailCarriesEventText1To9AndParameterTagVariants()
    {
        var alarm = Discrete("d");
        alarm.EventText1 = Texts("<body><p>one</p></body>", "<body><p>jeden</p></body>");
        alarm.EventText2 = Texts("<body><p/></body>", "<body><p/></body>");
        alarm.EventText9 = Texts("<body><p>nine</p></body>", "<body><p>dziewiec</p></body>");
        var software = SoftwareWith(new[] { alarm }, Array.Empty<HmiAnalogAlarm>());

        var detail = HmiAlarmReader.GetAlarm(software, "d", "discrete", null);

        Assert.True(detail.IsComplete);
        Assert.Empty(detail.Messages);
        Assert.Equal("d", detail.Alarm.Name);
        Assert.Equal("discrete", detail.Alarm.Kind);
        Assert.Equal("<body><p>one</p></body>", detail.EventText1![0].Text);
        Assert.Equal("<body><p/></body>", detail.EventText2![0].Text);
        Assert.Equal("<body><p>nine</p></body>", detail.EventText9![0].Text);
        Assert.Empty(detail.EventText3!);
        Assert.Equal(10, detail.AlarmParameterTags!.Count);
        Assert.Equal("p1", detail.AlarmParameterTags[0]);
        Assert.Equal("p10", detail.AlarmParameterTags[9]);
    }

    [Fact]
    public void DetailLanguageNarrowsEveryText()
    {
        var alarm = Analog("a");
        alarm.EventText3 = Texts("three", "trzy");
        var software = SoftwareWith(Array.Empty<HmiDiscreteAlarm>(), new[] { alarm });

        var detail = HmiAlarmReader.GetAlarm(software, "A", "analog", "pl-PL");

        Assert.Equal("trzy", Assert.Single(detail.EventText3!).Text);
        Assert.Equal("<body><p>wysoki</p></body>", Assert.Single(detail.Alarm.EventText!).Text);
    }

    [Fact]
    public void UnsupportedParameterTagShapeIsNullWithMessage()
    {
        var alarm = Analog("a");
        alarm.AlarmParameterTags = new object[] { "x", 1 };
        var software = SoftwareWith(Array.Empty<HmiDiscreteAlarm>(), new[] { alarm });

        var detail = HmiAlarmReader.GetAlarm(software, "a", "analog", null);

        Assert.Null(detail.AlarmParameterTags);
        Assert.False(detail.IsComplete);
        Assert.Contains(detail.Messages, m => m.Contains("alarm 'a'") && m.Contains("AlarmParameterTags"));
    }

    [Fact]
    public void UnreadableParameterTagsAreNullWithMessage()
    {
        var alarm = Discrete("d");
        alarm.Failures["AlarmParameterTags"] = new EngineeringTargetInvocationException("no tags");
        var software = SoftwareWith(new[] { alarm }, Array.Empty<HmiAnalogAlarm>());

        var detail = HmiAlarmReader.GetAlarm(software, "d", "discrete", null);

        Assert.Null(detail.AlarmParameterTags);
        Assert.False(detail.IsComplete);
    }

    [Fact]
    public void MissingAlarmIsTargetNotFound()
    {
        var software = Mixed();

        var missing = Assert.Throws<WorkerOperationException>(() => HmiAlarmReader.GetAlarm(software, "nope", "discrete", null));
        var wrongKind = Assert.Throws<WorkerOperationException>(() => HmiAlarmReader.GetAlarm(software, "a_an", "discrete", null));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, missing.FailureCategory);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, wrongKind.FailureCategory);
    }

    [Fact]
    public void DuplicateAlarmNameIsTargetAmbiguous()
    {
        var software = SoftwareWith(new[] { Discrete("dup"), Discrete("DUP") }, Array.Empty<HmiAnalogAlarm>());

        var ex = Assert.Throws<WorkerOperationException>(() => HmiAlarmReader.GetAlarm(software, "dup", "discrete", null));

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, ex.FailureCategory);
    }

    [Fact]
    public void ClassesReturnThreeArrays()
    {
        var software = Unified("Panel_RT");
        software.AlarmClasses.Items.Add(new HmiAlarmClass
        {
            Name = "b_class", Id = 2, Priority = 4, IsSystem = false, Log = "AlarmLog",
            StateMachine = HmiAlarmStateMachine.RaiseClearRequiresAcknowledgement, CommonAlarmClass = "Errors",
        });
        software.AlarmClasses.Items.Add(new HmiAlarmClass { Name = "A_class", Id = 1, IsSystem = true });
        software.HmiAlarmAuditClass.Items.Add(new HmiAlarmAuditClass { Name = "audit2" });
        software.HmiAlarmAuditClass.Items.Add(new HmiAlarmAuditClass { Name = "Audit1" });
        software.OpcUaAlarmTypes.Items.Add(new HmiOpcUaAlarmType
        {
            Name = "ua", AlarmClass = "Errors", Area = "Plant", ConditionTypeId = "ns=2;i=1", Connection = "OpcConn", NodeId = "ns=2;s=x",
        });

        var info = HmiAlarmReader.ListAlarmClasses(software);

        Assert.True(info.IsComplete);
        Assert.Equal(new[] { "A_class", "b_class" }, info.AlarmClasses.Select(c => c.Name));
        var b = info.AlarmClasses[1];
        Assert.Equal(2, b.Id);
        Assert.Equal(4, b.Priority);
        Assert.Equal(false, b.IsSystem);
        Assert.Equal("AlarmLog", b.Log);
        Assert.Equal("RaiseClearRequiresAcknowledgement", b.StateMachine);
        Assert.Equal("Errors", b.CommonAlarmClass);
        Assert.Equal(true, info.AlarmClasses[0].IsSystem);
        Assert.Equal(new[] { "Audit1", "audit2" }, info.AuditClasses.Select(c => c.Name));
        var ua = Assert.Single(info.OpcUaAlarmTypes);
        Assert.Equal("ua", ua.Name);
        Assert.Equal("Plant", ua.Area);
        Assert.Equal("ns=2;i=1", ua.ConditionTypeId);
        Assert.Equal("OpcConn", ua.Connection);
        Assert.Equal("ns=2;s=x", ua.NodeId);
    }

    [Fact]
    public void EmptyClassesAreThreeEmptyArrays()
    {
        var info = HmiAlarmReader.ListAlarmClasses(Unified("Panel_RT"));

        Assert.True(info.IsComplete);
        Assert.Empty(info.AlarmClasses);
        Assert.Empty(info.AuditClasses);
        Assert.Empty(info.OpcUaAlarmTypes);
    }

    [Fact]
    public void AuditClassPropertiesBeyondTheNameAreNeverRead()
    {
        var software = Unified("Panel_RT");
        var audit = new HmiAlarmAuditClass { Name = "audit" };
        software.HmiAlarmAuditClass.Items.Add(audit);

        HmiAlarmReader.ListAlarmClasses(software);

        Assert.Empty(audit.Reads);
    }

    [Fact]
    public void UnreadableClassPropertyIsNullWithMessageAndIncomplete()
    {
        var software = Unified("Panel_RT");
        var alarmClass = new HmiAlarmClass { Name = "c" };
        alarmClass.Failures["Log"] = new EngineeringTargetInvocationException("no log");
        software.AlarmClasses.Items.Add(alarmClass);
        var ua = new HmiOpcUaAlarmType { Name = "u" };
        ua.Failures["NodeId"] = new EngineeringNotSupportedException("no node id");
        software.OpcUaAlarmTypes.Items.Add(ua);

        var info = HmiAlarmReader.ListAlarmClasses(software);

        Assert.Null(info.AlarmClasses[0].Log);
        Assert.Null(info.OpcUaAlarmTypes[0].NodeId);
        Assert.False(info.IsComplete);
        Assert.Equal(2, info.Messages.Count);
    }

    [Fact]
    public void UnreadableClassCompositionFailsTheItem()
    {
        var software = Unified("Panel_RT");
        software.OpcUaAlarmTypes.EnumerationFailure = new EngineeringTargetInvocationException("opc ua unavailable");

        var ex = Assert.Throws<WorkerOperationException>(() => HmiAlarmReader.ListAlarmClasses(software));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
    }
}
