// Compile-only V21 declarations. No Siemens implementation code.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeSettingsCommon
{
    public enum BitNumberEvaluationType { ExactMatch, LeastSignificantBit }
    public enum CentralInteractionSetting { Customized, Enabled, Disabled }
    public enum GeneralESIGCommentsStrategy { BothComments, SignatureComment, OperationComment }
    public enum ScreenResolution { SR_1980X1280, SR_1920X1200, SR_1920X1080, SR_1280X800 }
    public enum StorageLocation { Local, Network }
    public enum GeneralPersistencyStrategy { Standard, Custom }
    public enum ExtendTextWith { None, Standard }
    public enum CriteriaAnalysisExtendedText { None, Standard }
}
namespace Siemens.Engineering.HmiUnified.RuntimeSettings
{
    public abstract class HmiRuntimeSetting : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public string AutoLogOffURL { get => throw new global::System.NotSupportedException(); }
        public bool BitSelection { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeSettingsCommon.BitNumberEvaluationType BitSelectionStrategyForResourceLists { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeSettingsCommon.BitNumberEvaluationType BitSelectionStrategyForTagDynamization { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeSettingsCommon.CentralInteractionSetting CentralInputHint { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeSettingsCommon.CentralInteractionSetting CentralPanning { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeSettingsCommon.CentralInteractionSetting CentralZooming { get => throw new global::System.NotSupportedException(); }
        public bool EnableLanguageCompatibleFontFamilies { get => throw new global::System.NotSupportedException(); }
        public bool GMPEnabled { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeSettingsCommon.GeneralESIGCommentsStrategy GeneralESIGCommentsStrategy { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiExclusiveOperationSettings HmiExclusiveOperationSettings { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiReportingSettings HmiReportingSettings { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiUnifiedTagSettings HmiUnifiedTagSettings { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiUpssRuntimeSettings HmiUpssRuntimeSettings { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiLanguageAndFontAssociation LanguageAndFonts { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiMaxLoginRuntimeSettings MaxLoginRuntimeSettings { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiOpcUaServerRuntimeSettings OpcUaServerRuntimeSettings { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiProcessDiagnosticsRuntimeSettings ProcessDiagnosticsRuntimeSettings { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeResourceSettings RuntimeResourceSettings { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeSettingsCommon.ScreenResolution ScreenResolution { get => throw new global::System.NotSupportedException(); }
        public string StartScreen { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiTelemetryRuntimeSettings TelemetryRuntimeSettings { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class HmiExclusiveOperationSettings : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public string ExclusiveOperationControlTag { get => throw new global::System.NotSupportedException(); }
        public string ExclusiveOperationStatusTag { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class HmiReportingSettings : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public bool IsReportingEnabled { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeSettingsCommon.StorageLocation ReportingDatabaseStorage { get => throw new global::System.NotSupportedException(); }
        public string ReportingDatabaseStoragePath { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeSettingsCommon.StorageLocation ReportingMainStorage { get => throw new global::System.NotSupportedException(); }
        public string ReportingMainStoragePath { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class HmiUnifiedTagSettings : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public bool IgnoreInitialQCNotifications { get => throw new global::System.NotSupportedException(); }
        public bool IgnoreTimestampNotifications { get => throw new global::System.NotSupportedException(); }
        public bool TagOptimizationActive { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class HmiUpssRuntimeSettings : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public string GlobalScopePersistencyAuthorization { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeSettingsCommon.GeneralPersistencyStrategy PersistencyStrategy { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class HmiMaxLoginRuntimeSettings : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public bool EnableLockAfterNumberOfAttempts { get => throw new global::System.NotSupportedException(); }
        public uint MaxLoginErrors { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class HmiProcessDiagnosticsRuntimeSettings : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public bool CriteriaAnalysisAbsoluteAddress { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeSettingsCommon.ExtendTextWith CriteriaAnalysisAll { get => throw new global::System.NotSupportedException(); }
        public bool CriteriaAnalysisComment { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeSettingsCommon.CriteriaAnalysisExtendedText CriteriaAnalysisExtendText { get => throw new global::System.NotSupportedException(); }
        public bool CriteriaAnalysisSymbol { get => throw new global::System.NotSupportedException(); }
        public bool CriteriaAnalysisValue { get => throw new global::System.NotSupportedException(); }
        public bool EnableProcessDiagnostics { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class HmiRuntimeResourceSettings : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public bool EnableHighResolutionGraphicsOptimization { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class HmiTelemetryRuntimeSettings : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public bool TelemetryActive { get => throw new global::System.NotSupportedException(); }
        public string TelemetryStorageFolder { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeSettingsCommon.StorageLocation TelemetryStorageMedia { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class HmiOpcUaServerRuntimeSettings : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public bool ActAsOPCServer { get => throw new global::System.NotSupportedException(); }
        public int EndpointUrlPortNumber { get => throw new global::System.NotSupportedException(); }
        public bool EnableGuestAuthentication { get => throw new global::System.NotSupportedException(); }
        public bool EnableUsernameAndPasswordAuthentication { get => throw new global::System.NotSupportedException(); }
        public int MaxSessionCount { get => throw new global::System.NotSupportedException(); }
        public bool OpcUaServerSecurityNone { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class HmiLanguageAndFont : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public string Language { get => throw new global::System.NotSupportedException(); }
        public short Order { get => throw new global::System.NotSupportedException(); }
        public bool Enable { get => throw new global::System.NotSupportedException(); }
        public bool EnableForLogging { get => throw new global::System.NotSupportedException(); }
        public string DefaultFont { get => throw new global::System.NotSupportedException(); }
        public string FixedFont1 { get => throw new global::System.NotSupportedException(); }
        public string FixedFont2 { get => throw new global::System.NotSupportedException(); }
        public string FixedFont3 { get => throw new global::System.NotSupportedException(); }
        public string FixedFont4 { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class HmiLanguageAndFontAssociation : global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiLanguageAndFont>
    {
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.HmiUnified.RuntimeSettings.HmiLanguageAndFont> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
}
