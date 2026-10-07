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

    public sealed class HmiConnection : PropertyBagNamed
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

    public abstract class AlarmBase : PropertyBagNamed
    {
        public MultilingualText EventText { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText EventText1 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText EventText2 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText EventText3 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText EventText4 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText EventText5 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText EventText6 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText EventText7 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText EventText8 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText EventText9 { get => Get<MultilingualText>(); set => Set(value); }
        public MultilingualText InfoText { get => Get<MultilingualText>(); set => Set(value); }
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
    public sealed class HmiAlarmClass : PropertyBagNamed
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
        public string AcknowledgmentControlTag { get => Get<string>(); set => Set(value); }
        public int AcknowledgmentControlTagBitNumber { get => Get<int>(); set => Set(value); }
        public string AcknowledgmentStateTag { get => Get<string>(); set => Set(value); }
        public int AcknowledgmentStateTagBitNumber { get => Get<int>(); set => Set(value); }
        public uint RaisedStateTagBitNumber { get => Get<uint>(); set => Set(value); }
        public HmiAlarmCommon.HmiDiscreteAlarmTriggerMode TriggerMode { get => Get<HmiAlarmCommon.HmiDiscreteAlarmTriggerMode>(); set => Set(value); }
    }

    public sealed class HmiDiscreteAlarmComposition : Composition<HmiDiscreteAlarm> { }

    public sealed class HmiAnalogAlarm : HmiAlarmCommon.AlarmBase
    {
        public HmiAlarmCommon.HmiAlarmCondition Condition { get => Get<HmiAlarmCommon.HmiAlarmCondition>(); set => Set(value); }
        public object ConditionValue { get => Get<object>(); set => Set(value); }
    }

    public sealed class HmiAnalogAlarmComposition : Composition<HmiAnalogAlarm> { }
}

namespace Siemens.Engineering.HmiUnified.HmiAudit
{
    public sealed class HmiAlarmAuditClass : PropertyBagNamed { }

    public sealed class HmiAlarmAuditClassComposition : Composition<HmiAlarmAuditClass> { }
}

namespace Siemens.Engineering.HmiUnified.HmiOpcUaAlarm
{
    public sealed class HmiOpcUaAlarmType : PropertyBagNamed
    {
        public string AlarmClass { get => Get<string>(); set => Set(value); }
        public string Area { get => Get<string>(); set => Set(value); }
        public string ConditionTypeId { get => Get<string>(); set => Set(value); }
        public string Connection { get => Get<string>(); set => Set(value); }
        public string NodeId { get => Get<string>(); set => Set(value); }
    }

    public sealed class HmiOpcUaAlarmTypeComposition : Composition<HmiOpcUaAlarmType> { }
}
