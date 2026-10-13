// Offline boundary doubles for the hmi_read connection and alarm readers. They declare the same members as
// reference-stubs/Siemens.Engineering.WinCCUnified/{Connections,Alarms}.cs and add test hooks; no Openness
// assembly is loaded. Keep this file in step with the stubs.
using Siemens.Engineering.HmiUnified.HmiTags;

namespace Siemens.Engineering.HmiUnified
{
    public sealed partial class HmiSoftware
    {
        public HmiConnections.HmiConnectionComposition Connections { get; } = new();
        public HmiAlarm.HmiAlarmClassComposition AlarmClasses { get; } = new();
        public HmiAlarm.HmiAnalogAlarmComposition AnalogAlarms { get; } = new();
        public HmiAlarm.HmiDiscreteAlarmComposition DiscreteAlarms { get; } = new();
        public HmiAudit.HmiAlarmAuditClassComposition HmiAlarmAuditClass { get; } = new();
        public HmiOpcUaAlarm.HmiOpcUaAlarmTypeComposition OpcUaAlarmTypes { get; } = new();
    }
}

namespace Siemens.Engineering.HmiUnified.HmiConnections
{
    public sealed class DriverProperty : PropertyBagNamed
    {
        public string Info { get => Get<string>(); set => Set(value); }
        public string PropertyName { get => Get<string>(); set => Set(value); }
        public string Value { get => Get<string>(); set => Set(value); }
    }

    public sealed class DriverPropertyComposition : Composition<DriverProperty> { }

    public sealed class HmiConnection : Common.ValidatableBag
    {
        private readonly DriverPropertyComposition driverProperties = new();

        public DriverPropertyComposition DriverProperties { get { Check(nameof(DriverProperties)); return driverProperties; } }
        public string Comment { get => Get<string>(); set => Set(value); }
        public string CommunicationDriver { get => Get<string>(); set => Set(value); }
        public bool DisabledAtStartup { get => Get<bool>(); set => Set(value); }
        public string InitialAddress { get => Get<string>(); set => Set(value); }
        public string Node { get => Get<string>(); set => Set(value); }
        public string Partner { get => Get<string>(); set => Set(value); }
        public string Station { get => Get<string>(); set => Set(value); }
    }

    public sealed class HmiConnectionComposition : Composition<HmiConnection> { }
}

namespace Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon
{
    public enum HmiAlarmCondition { LowerLimit, UpperLimit, Equal, NotEqual, LowerLimitOrEqual, UpperLimitOrEqual }
    public enum HmiAlarmStateMachine { Raise, RaiseClear, RaiseRequiresAcknowledgement, RaiseClearOptionalAcknowledgement, RaiseClearRequiresAcknowledgement, RaiseClearRequiresAcknowledgementAndReset }
    public enum HmiDiscreteAlarmTriggerMode { OnRisingEdge, OnFallingEdge }

    public abstract class AlarmBase : Common.ValidatableBag
    {
        public new string Name { get => base.Name; set { Set(value); base.Name = value; } }
        public Action? DeleteAction { get; set; }
        public void Delete() => DeleteAction?.Invoke();
        public MultilingualText EventText { get => Get<MultilingualText>() ?? eventText; set => Set(value); }
        private readonly MultilingualText eventText = new MultilingualText().With("en-US", "");
        public MultilingualText EventText1 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText EventText2 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText EventText3 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText EventText4 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText EventText5 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText EventText6 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText EventText7 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText EventText8 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText EventText9 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText InfoText { get => Get<MultilingualText>() ?? infoText; set => Set(value); }
        private readonly MultilingualText infoText = new MultilingualText().With("en-US", "");
        public string AlarmClass { get => Get<string>(); set => Set(value); }
        public object AlarmParameterTags { get => Get<object>(); set => Set(value); }
        public string Area { get => Get<string>(); set => Set(value); }
        public byte Priority { get => Get<byte>(); set => Set(value); }
        public string Origin { get => Get<string>(); set => Set(value); }
        public string RaisedStateTag { get => Get<string>(); set => Set(value); }
    }
}

namespace Siemens.Engineering.HmiUnified.HmiAlarm
{
    public sealed class HmiAlarmClass : Common.ValidatableBag
    {
        public string CommonAlarmClass { get => Get<string>(); set => Set(value); }
        public uint Id { get => Get<uint>(); set => Set(value); }
        public bool IsSystem { get => Get<bool>(); set => Set(value); }
        public string Log { get => Get<string>(); set => Set(value); }
        public byte Priority { get => Get<byte>(); set => Set(value); }
        public HmiAlarmCommon.HmiAlarmStateMachine StateMachine { get => Get<HmiAlarmCommon.HmiAlarmStateMachine>(); set => Set(value); }
    }

    public sealed class HmiAlarmClassComposition : Composition<HmiAlarmClass> { }

    public sealed class HmiDiscreteAlarm : HmiAlarmCommon.AlarmBase
    {
        public string TriggerBitAddress { get => Get<string>(); set => Set(value); }
        public string AcknowledgmentControlTag { get => Get<string>(); set => Set(value); }
        public int AcknowledgmentControlTagBitNumber { get => Get<int>(); set => Set(value); }
        public string AcknowledgmentStateTag { get => Get<string>(); set => Set(value); }
        public int AcknowledgmentStateTagBitNumber { get => Get<int>(); set => Set(value); }
        public uint RaisedStateTagBitNumber { get => Get<uint>(); set => Set(value); }
        public HmiAlarmCommon.HmiDiscreteAlarmTriggerMode TriggerMode { get => Get<HmiAlarmCommon.HmiDiscreteAlarmTriggerMode>(); set => Set(value); }
    }

    public sealed class HmiDiscreteAlarmComposition : Composition<HmiDiscreteAlarm>
    {
        public Action<HmiDiscreteAlarm>? Created { get; set; }
        public Func<string, HmiDiscreteAlarm?>? FindOverride { get; set; }
        public new HmiDiscreteAlarm? Find(string name) => FindOverride is null ? Items.FirstOrDefault(x => x.Name == name) : FindOverride(name);
        public HmiDiscreteAlarm Create(string name)
        {
            if (Items.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) throw new EngineeringException("Alarm already exists.");
            var alarm = new HmiDiscreteAlarm { Name = name };
            alarm.Writes.Clear();
            alarm.DeleteAction = () => Items.Remove(alarm);
            Items.Add(alarm); Created?.Invoke(alarm); return alarm;
        }
    }

    public sealed class HmiAnalogAlarm : HmiAlarmCommon.AlarmBase
    {
        public string TriggerAddress { get => Get<string>(); set => Set(value); }
        public HmiAlarmCommon.HmiAlarmCondition Condition { get => Get<HmiAlarmCommon.HmiAlarmCondition>(); set => Set(value); }
        public object ConditionValue { get => Get<object>(); set => Set(value); }
    }

    public sealed class HmiAnalogAlarmComposition : Composition<HmiAnalogAlarm>
    {
        public Action<HmiAnalogAlarm>? Created { get; set; }
        public Func<string, HmiAnalogAlarm?>? FindOverride { get; set; }
        public new HmiAnalogAlarm? Find(string name) => FindOverride is null ? Items.FirstOrDefault(x => x.Name == name) : FindOverride(name);
        public HmiAnalogAlarm Create(string name)
        {
            if (Items.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) throw new EngineeringException("Alarm already exists.");
            var alarm = new HmiAnalogAlarm { Name = name };
            alarm.Writes.Clear();
            alarm.DeleteAction = () => Items.Remove(alarm);
            Items.Add(alarm); Created?.Invoke(alarm); return alarm;
        }
    }
}

namespace Siemens.Engineering.HmiUnified.HmiAudit
{
    public sealed class HmiAlarmAuditClass : Common.ValidatableBag { }

    public sealed class HmiAlarmAuditClassComposition : Composition<HmiAlarmAuditClass> { }
}

namespace Siemens.Engineering.HmiUnified.HmiOpcUaAlarm
{
    public sealed class HmiOpcUaAlarmType : Common.ValidatableBag
    {
        public string AlarmClass { get => Get<string>(); set => Set(value); }
        public string Area { get => Get<string>(); set => Set(value); }
        public string ConditionTypeId { get => Get<string>(); set => Set(value); }
        public string Connection { get => Get<string>(); set => Set(value); }
        public string NodeId { get => Get<string>(); set => Set(value); }
    }

    public sealed class HmiOpcUaAlarmTypeComposition : Composition<HmiOpcUaAlarmType> { }
}
