// Compile-only V21 declarations. No Siemens implementation code.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering.HmiUnified
{
    public abstract partial class HmiSoftware
    {
        public global::Siemens.Engineering.HmiUnified.HmiLogging.HmiDataLogComposition DataLogs { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.HmiLogging.HmiAlarmLogComposition AlarmLogs { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.HmiLogging.HmiAuditTrailComposition AuditTrails { get => throw new global::System.NotSupportedException(); }
    }
}
namespace Siemens.Engineering.HmiUnified.HmiLogging.HmiLoggingCommon
{
    public enum DeviceNode { None, Off, Default, Local, SDX51, USBX61, USBX62 }
    public enum HmiBackupMode { NoBackup, PrimaryPath }
    public abstract class LogDuration : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public uint Days { get => throw new global::System.NotSupportedException(); }
        public uint Hours { get => throw new global::System.NotSupportedException(); }
        public uint Minutes { get => throw new global::System.NotSupportedException(); }
        public uint Seconds { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class SegmentDuration : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public uint Days { get => throw new global::System.NotSupportedException(); }
        public uint Hours { get => throw new global::System.NotSupportedException(); }
        public uint Minutes { get => throw new global::System.NotSupportedException(); }
        public uint Seconds { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class LogSettings : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public uint LogMaxSize { get => throw new global::System.NotSupportedException(); }
        public LogDuration LogTimePeriod { get => throw new global::System.NotSupportedException(); }
        public DeviceNode StorageDevice { get => throw new global::System.NotSupportedException(); }
        public string StorageFolder { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class LogSegment : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public uint SegmentMaxSize { get => throw new global::System.NotSupportedException(); }
        public global::System.DateTime SegmentStartTime { get => throw new global::System.NotSupportedException(); }
        public SegmentDuration SegmentTimePeriod { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class LogBackup : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public HmiBackupMode BackupMode { get => throw new global::System.NotSupportedException(); }
        public string PrimaryPath { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class LoggingBase : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::Siemens.Engineering.HmiUnified.Common.IValidator
    {
        public global::System.Collections.Generic.IList<global::Siemens.Engineering.HmiUnified.Common.HmiValidationResult> Validate() => throw new global::System.NotSupportedException();
        public LogBackup Backup { get => throw new global::System.NotSupportedException(); }
        public LogSegment Segment { get => throw new global::System.NotSupportedException(); }
        public LogSettings Settings { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
}
namespace Siemens.Engineering.HmiUnified.HmiLogging
{
    public abstract class HmiDataLog : global::Siemens.Engineering.HmiUnified.HmiLogging.HmiLoggingCommon.LoggingBase
    {
        public string Name { get => throw new global::System.NotSupportedException(); }
    }
    public abstract class HmiDataLogComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<HmiDataLog>
    {
        public global::System.Collections.Generic.IEnumerator<HmiDataLog> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class HmiAlarmLog : global::Siemens.Engineering.HmiUnified.HmiLogging.HmiLoggingCommon.LoggingBase
    {
        public string Name { get => throw new global::System.NotSupportedException(); }
    }
    public abstract class HmiAlarmLogComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<HmiAlarmLog>
    {
        public global::System.Collections.Generic.IEnumerator<HmiAlarmLog> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class HmiAuditTrail : global::Siemens.Engineering.HmiUnified.HmiLogging.HmiLoggingCommon.LoggingBase
    {
        public string Name { get => throw new global::System.NotSupportedException(); }
    }
    public abstract class HmiAuditTrailComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<HmiAuditTrail>
    {
        public global::System.Collections.Generic.IEnumerator<HmiAuditTrail> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
}
