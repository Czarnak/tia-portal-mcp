using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;
using static TiaMcpServer.Tests.Hmi.HmiTestProjects;
namespace TiaMcpServer.Tests.Hmi;
[Collection(HmiSnapshotStoreCollection.Name)]
public class HmiAlarmWriterTests
{
    private static HmiWriteRequestInfo Request(string kind = "discrete") => new() { HmiName = "Runtime", AlarmName = "Alarm", AlarmKind = kind };
    [Fact] public void CreateDiscrete_SetsInSpecOrder()
    {
        var software = new HmiSoftware(); var request = Request();
        request.TriggerTag = "Trigger"; request.BitNumber = 3; request.TriggerMode = "OnFallingEdge";
        request.AlarmClass = "Warning"; request.Priority = 255;
        request.EventText = new() { new("en-US", "Event") }; request.InfoText = new() { new("en-US", "Info") };
        var outcome = HmiAlarmWriter.Create(software, request);
        Assert.Equal(new[] { "Create", "RaisedStateTag", "RaisedStateTagBitNumber", "TriggerMode", "AlarmClass", "Priority", "EventText", "InfoText" }, outcome.AppliedProperties);
        var alarm = software.DiscreteAlarms.Find("Alarm")!;
        Assert.Equal(new[] { "RaisedStateTag", "RaisedStateTagBitNumber", "TriggerMode", "AlarmClass", "Priority" }, alarm.Writes);
        Assert.Equal("Trigger", alarm.RaisedStateTag); Assert.Equal(3u, alarm.RaisedStateTagBitNumber);
        Assert.Equal("OnFallingEdge", alarm.TriggerMode.ToString()); Assert.Equal("Warning", alarm.AlarmClass); Assert.Equal((byte)255, alarm.Priority);
        Assert.Equal("Event", alarm.EventText.Items.Single().Text); Assert.Equal("Info", alarm.InfoText.Items.Single().Text);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void CreateAnalog_SetsTriggerBeforeConditionValue_AndRetainsNumericType(bool integral)
    {
        var software = new HmiSoftware(); var request = Request("analog");
        request.TriggerTag = "Trigger"; request.Condition = "UpperLimit";
        if (integral) request.ConditionValueInt32 = 50; else request.ConditionValueDouble = 50.5;
        request.AlarmClass = "Warning"; request.Priority = 0;
        request.EventText = new() { new("en-US", "Event") }; request.InfoText = new() { new("en-US", "Info") };
        var outcome = HmiAlarmWriter.Create(software, request);
        Assert.Equal(new[] { "Create", "RaisedStateTag", "Condition", "ConditionValue", "AlarmClass", "Priority", "EventText", "InfoText" }, outcome.AppliedProperties);
        var alarm = software.AnalogAlarms.Find("Alarm")!;
        Assert.Equal(new[] { "RaisedStateTag", "Condition", "ConditionValue", "AlarmClass", "Priority" }, alarm.Writes);
        if (integral) Assert.Equal(50, Assert.IsType<int>(alarm.ConditionValue)); else Assert.Equal(50.5, Assert.IsType<double>(alarm.ConditionValue));
        Assert.Equal("UpperLimit", alarm.Condition.ToString()); Assert.Equal("Trigger", alarm.RaisedStateTag);
        Assert.Equal("Event", alarm.EventText.Items.Single().Text); Assert.Equal("Info", alarm.InfoText.Items.Single().Text);
    }
    [Theory] [InlineData("discrete")] [InlineData("analog")]
    public void UpdateAlarm_RenamesFirst_AndStopsAtFailure(string kind)
    {
        var software = new HmiSoftware(); var request = Request(kind);
        HmiAlarmWriter.Create(software, request);
        var alarm = kind == "discrete" ? (Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon.AlarmBase)software.DiscreteAlarms.Find("Alarm")! : software.AnalogAlarms.Find("Alarm")!;
        alarm.SetFailures["RaisedStateTag"] = new EngineeringException("Rejected");
        request.NewName = "Renamed"; request.TriggerTag = "Missing"; request.Priority = 2;
        var error = Assert.Throws<WorkerOperationException>(() => HmiAlarmWriter.Update(software, request));
        Assert.Equal("Renamed", alarm.Name); Assert.Equal("Renamed", error.HmiWriteOutcome!.ObjectName);
        Assert.Equal(new[] { "Name" }, error.HmiWriteOutcome.AppliedProperties); Assert.Equal("RaisedStateTag", error.HmiWriteOutcome.FailedProperty);
        Assert.Equal((byte)0, alarm.Priority);
    }
    [Theory] [InlineData("discrete", "deleted")] [InlineData("analog", "deleted")]
    [InlineData("discrete", "notNeeded")] [InlineData("analog", "notNeeded")]
    [InlineData("discrete", "failed")] [InlineData("analog", "failed")]
    [InlineData("discrete", "identity")] [InlineData("analog", "identity")]
    public void FailedCreate_CleansOnlyOwnedObject(string kind, string cleanup)
    {
        var software = new HmiSoftware(); var deletedReplacement = false;
        var discreteReplacement = new HmiDiscreteAlarm { Name = "Alarm", DeleteAction = () => deletedReplacement = true };
        var analogReplacement = new HmiAnalogAlarm { Name = "Alarm", DeleteAction = () => deletedReplacement = true };
        void Configure(Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon.AlarmBase alarm)
        {
            alarm.SetFailures["AlarmClass"] = new EngineeringException("Rejected");
            if (cleanup == "failed") alarm.DeleteAction = () => throw new EngineeringException("Cleanup rejected");
        }
        software.DiscreteAlarms.Created = Configure; software.AnalogAlarms.Created = Configure;
        if (cleanup == "notNeeded") { software.DiscreteAlarms.FindOverride = _ => null; software.AnalogAlarms.FindOverride = _ => null; }
        if (cleanup == "identity") { software.DiscreteAlarms.FindOverride = _ => discreteReplacement; software.AnalogAlarms.FindOverride = _ => analogReplacement; }
        var request = Request(kind); request.TriggerTag = "Trigger"; request.AlarmClass = "Missing"; request.Priority = 2;
        var error = Assert.Throws<WorkerOperationException>(() => HmiAlarmWriter.Create(software, request));
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, error.FailureCategory);
        Assert.Equal("AlarmClass", error.HmiWriteOutcome!.FailedProperty); Assert.Equal(new[] { "Create", "RaisedStateTag" }, error.HmiWriteOutcome.AppliedProperties);
        Assert.Equal(cleanup == "identity" ? "failed" : cleanup, error.HmiWriteOutcome.Cleanup); Assert.False(deletedReplacement);
        if (cleanup == "deleted") { Assert.Empty(software.DiscreteAlarms.Items); Assert.Empty(software.AnalogAlarms.Items); }
        if (cleanup is "failed" or "identity") Assert.NotNull(error.HmiWriteOutcome.CleanupMessage);
    }
    [Theory] [InlineData("discrete")] [InlineData("analog")]
    public void Collision_DoesNotDeleteExistingAlarm(string kind)
    {
        var software = new HmiSoftware(); var request = Request(kind); HmiAlarmWriter.Create(software, request);
        var error = Assert.Throws<WorkerOperationException>(() => HmiAlarmWriter.Create(software, request));
        Assert.Equal("notNeeded", error.HmiWriteOutcome!.Cleanup);
        Assert.Equal(1, kind == "discrete" ? software.DiscreteAlarms.Items.Count : software.AnalogAlarms.Items.Count);
    }
    [Theory] [InlineData("discrete")] [InlineData("analog")]
    public void Delete_UsesSeparateKindScope(string kind)
    {
        var software = new HmiSoftware(); HmiAlarmWriter.Create(software, Request()); HmiAlarmWriter.Create(software, Request("analog"));
        HmiAlarmWriter.Delete(software, Request(kind));
        Assert.Equal(kind == "discrete" ? 0 : 1, software.DiscreteAlarms.Items.Count);
        Assert.Equal(kind == "analog" ? 0 : 1, software.AnalogAlarms.Items.Count);
    }
    [Theory]
    [InlineData("discrete", false)] [InlineData("analog", false)]
    [InlineData("discrete", true)] [InlineData("analog", true)]
    public void MissingTextCulture_PreservesCategoryAndOutcome(string kind, bool update)
    {
        var software = new HmiSoftware(); var request = Request(kind);
        if (update) HmiAlarmWriter.Create(software, request);
        request.TriggerTag = "Trigger";
        request.EventText = new() { new("en-US", "Applied"), new("de-DE", "Missing") };
        var error = Assert.Throws<WorkerOperationException>(() => update
            ? HmiAlarmWriter.Update(software, request) : HmiAlarmWriter.Create(software, request));
        Assert.Equal(WorkerFailureCategories.TargetNotFound, error.FailureCategory);
        Assert.Equal("Text culture 'de-DE' was not found.", error.Message);
        Assert.Equal("Alarm", error.HmiWriteOutcome!.ObjectName);
        Assert.Equal(update ? new[] { "RaisedStateTag" } : new[] { "Create", "RaisedStateTag" }, error.HmiWriteOutcome.AppliedProperties);
        Assert.Equal("EventText", error.HmiWriteOutcome.FailedProperty);
        Assert.Equal(update ? null : "deleted", error.HmiWriteOutcome.Cleanup);
        if (update)
        {
            var alarm = kind == "discrete"
                ? (Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon.AlarmBase)software.DiscreteAlarms.Find("Alarm")!
                : software.AnalogAlarms.Find("Alarm")!;
            Assert.Equal("Trigger", alarm.RaisedStateTag);
            Assert.Equal("Applied", alarm.EventText.Items.Single().Text);
        }
        else { Assert.Empty(software.DiscreteAlarms.Items); Assert.Empty(software.AnalogAlarms.Items); }
    }
    [Theory] [InlineData("discrete")] [InlineData("analog")]
    public void Delete_PreservesCategorizedFailureAndWarnings(string kind)
    {
        var software = new HmiSoftware(); var request = Request(kind); HmiAlarmWriter.Create(software, request);
        var alarm = kind == "discrete"
            ? (Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon.AlarmBase)software.DiscreteAlarms.Find("Alarm")!
            : software.AnalogAlarms.Find("Alarm")!;
        alarm.DeleteAction = () => throw new WorkerOperationException(
            WorkerFailureCategories.TargetAmbiguous, "Identity changed", new[] { "Original warning" });
        var error = Assert.Throws<WorkerOperationException>(() => HmiAlarmWriter.Delete(software, request));
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, error.FailureCategory);
        Assert.Equal("Identity changed", error.Message);
        Assert.Equal(new[] { "Original warning" }, error.Warnings);
        Assert.Equal("Delete", error.HmiWriteOutcome!.FailedProperty);
        Assert.Empty(error.HmiWriteOutcome.AppliedProperties);
        Assert.Equal(1, kind == "discrete" ? software.DiscreteAlarms.Items.Count : software.AnalogAlarms.Items.Count);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Dispatch_DropsOnlyChangedHmiSnapshots_EvenAfterFailure(bool fails)
    {
        HmiSoftwareLocator.ResetCache(); var software = Unified("Runtime"); var other = Unified("Other");
        var project = ProjectWith(DeviceWith("Panel", software), DeviceWith("OtherPanel", other));
        var id = HmiSnapshotStore.Shared.Put(project, software, "hmi_list_alarms", new object[] { "Alarm" });
        var retained = HmiSnapshotStore.Shared.Put(project, other, "hmi_list_alarms", new object[] { "X" });
        if (fails) software.DiscreteAlarms.Created = a => a.SetFailures["RaisedStateTag"] = new EngineeringException("Rejected");
        var request = Request(); request.TriggerTag = "Trigger";
        if (fails) Assert.Throws<WorkerOperationException>(() => HmiWriteDispatch.Write(project, "hmi_create_alarm", request));
        else HmiWriteDispatch.Write(project, "hmi_create_alarm", request);
        Assert.Throws<WorkerOperationException>(() => HmiSnapshotStore.Shared.Take(project, id, "hmi_list_alarms"));
        Assert.Same(other, HmiSnapshotStore.Shared.Take(project, retained, "hmi_list_alarms").Software);
    }
}
