// Compile-only V21 declarations. No Siemens implementation code.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering.HmiUnified
{
    public abstract partial class HmiSoftware
    {
        public global::Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmClassComposition AlarmClasses { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.HmiAlarm.HmiAnalogAlarmComposition AnalogAlarms { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.HmiAlarm.HmiDiscreteAlarmComposition DiscreteAlarms { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.HmiAudit.HmiAlarmAuditClassComposition HmiAlarmAuditClass { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.HmiOpcUaAlarm.HmiOpcUaAlarmTypeComposition OpcUaAlarmTypes { get => throw new global::System.NotSupportedException(); }
    }
}
namespace Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon
{
    public enum HmiAlarmCondition { LowerLimit, UpperLimit, Equal, NotEqual, LowerLimitOrEqual, UpperLimitOrEqual }
    public enum HmiAlarmStateMachine { Raise, RaiseClear, RaiseRequiresAcknowledgement, RaiseClearOptionalAcknowledgement, RaiseClearRequiresAcknowledgement, RaiseClearRequiresAcknowledgementAndReset }
    public enum HmiDiscreteAlarmTriggerMode { OnRisingEdge, OnFallingEdge }
    public abstract class AlarmBase : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::Siemens.Engineering.MultilingualText EventText { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.MultilingualText EventText1 { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.MultilingualText EventText2 { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.MultilingualText EventText3 { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.MultilingualText EventText4 { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.MultilingualText EventText5 { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.MultilingualText EventText6 { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.MultilingualText EventText7 { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.MultilingualText EventText8 { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.MultilingualText EventText9 { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.MultilingualText InfoText { get => throw new global::System.NotSupportedException(); }
        public string AlarmClass { get => throw new global::System.NotSupportedException(); }
        public object AlarmParameterTags { get => throw new global::System.NotSupportedException(); }
        public string Area { get => throw new global::System.NotSupportedException(); }
        public byte Priority { get => throw new global::System.NotSupportedException(); }
        public string Origin { get => throw new global::System.NotSupportedException(); }
        public string RaisedStateTag { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
}
namespace Siemens.Engineering.HmiUnified.HmiAlarm
{
    public abstract class HmiAlarmClass : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public string CommonAlarmClass { get => throw new global::System.NotSupportedException(); }
        public uint Id { get => throw new global::System.NotSupportedException(); }
        public bool IsSystem { get => throw new global::System.NotSupportedException(); }
        public string Log { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
        public byte Priority { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon.HmiAlarmStateMachine StateMachine { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class HmiAlarmClassComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<HmiAlarmClass>
    {
        public global::System.Collections.Generic.IEnumerator<HmiAlarmClass> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class HmiDiscreteAlarm : global::Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon.AlarmBase
    {
        public string AcknowledgmentControlTag { get => throw new global::System.NotSupportedException(); }
        public int AcknowledgmentControlTagBitNumber { get => throw new global::System.NotSupportedException(); }
        public string AcknowledgmentStateTag { get => throw new global::System.NotSupportedException(); }
        public int AcknowledgmentStateTagBitNumber { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
        public uint RaisedStateTagBitNumber { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon.HmiDiscreteAlarmTriggerMode TriggerMode { get => throw new global::System.NotSupportedException(); }
    }
    public abstract class HmiDiscreteAlarmComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<HmiDiscreteAlarm>
    {
        public global::System.Collections.Generic.IEnumerator<HmiDiscreteAlarm> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class HmiAnalogAlarm : global::Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon.AlarmBase
    {
        public global::Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon.HmiAlarmCondition Condition { get => throw new global::System.NotSupportedException(); }
        public object ConditionValue { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
    }
    public abstract class HmiAnalogAlarmComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<HmiAnalogAlarm>
    {
        public global::System.Collections.Generic.IEnumerator<HmiAnalogAlarm> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
}
namespace Siemens.Engineering.HmiUnified.HmiAudit
{
    public abstract class HmiAuditClass : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public string Name { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class HmiAlarmAuditClass : global::Siemens.Engineering.HmiUnified.HmiAudit.HmiAuditClass
    {
    }
    public abstract class HmiAlarmAuditClassComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<HmiAlarmAuditClass>
    {
        public global::System.Collections.Generic.IEnumerator<HmiAlarmAuditClass> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
}
namespace Siemens.Engineering.HmiUnified.HmiOpcUaAlarm
{
    public abstract class HmiOpcUaAlarmType : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public string AlarmClass { get => throw new global::System.NotSupportedException(); }
        public string Area { get => throw new global::System.NotSupportedException(); }
        public string ConditionTypeId { get => throw new global::System.NotSupportedException(); }
        public string Connection { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
        public string NodeId { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class HmiOpcUaAlarmTypeComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<HmiOpcUaAlarmType>
    {
        public global::System.Collections.Generic.IEnumerator<HmiOpcUaAlarmType> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
}
