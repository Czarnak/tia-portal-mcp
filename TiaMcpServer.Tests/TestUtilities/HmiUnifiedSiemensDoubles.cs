// Offline boundary doubles for the hmi_read readers. They declare the same members as
// reference-stubs/Siemens.Engineering.WinCCUnified (and the Base members the readers touch) and add test
// hooks; no Openness assembly is loaded. Keep this file in step with the stub.
using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Siemens.Engineering
{
    public sealed class EngineeringTargetInvocationException : EngineeringException
    {
        public EngineeringTargetInvocationException(string message) : base(message) { }
    }

    public sealed class EngineeringObjectDisposedException : EngineeringException
    {
        public EngineeringObjectDisposedException(string message) : base(message) { }
    }

    public sealed class Language
    {
        public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;
        public Exception? CultureFailure { get; set; }
        public static Language Of(string culture) => new() { Culture = CultureInfo.GetCultureInfo(culture) };
    }

    public sealed class LanguageAssociation : IEnumerable<Language>
    {
        public List<Language> Items { get; } = new();
        public Exception? EnumerationFailure { get; set; }
        public IEnumerator<Language> GetEnumerator()
        {
            if (EnumerationFailure is not null) throw EnumerationFailure;
            return Items.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class LanguageSettings
    {
        private Language? editing, reference;
        public LanguageAssociation ActiveLanguages { get; } = new();
        public Exception? EditingLanguageFailure { get; set; }
        public Exception? ReferenceLanguageFailure { get; set; }
        public Language EditingLanguage
        {
            get => EditingLanguageFailure is null ? editing! : throw EditingLanguageFailure;
            set => editing = value;
        }
        public Language ReferenceLanguage
        {
            get => ReferenceLanguageFailure is null ? reference! : throw ReferenceLanguageFailure;
            set => reference = value;
        }
    }

    public sealed partial class Project
    {
        public LanguageSettings LanguageSettings { get; set; } = new();
    }

    public sealed class MultilingualTextItem
    {
        public Language Language { get; set; } = new();
        public string Text { get; set; } = string.Empty;
        public Exception? TextFailure { get; set; }
    }

    public sealed class MultilingualTextItemComposition : IEnumerable<MultilingualTextItem>
    {
        public List<MultilingualTextItem> Items { get; } = new();
        public IEnumerator<MultilingualTextItem> GetEnumerator() => Items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class MultilingualText
    {
        public MultilingualTextItemComposition Items { get; } = new();
        public MultilingualText With(string culture, string text)
        {
            Items.Items.Add(new MultilingualTextItem { Language = Language.Of(culture), Text = text });
            return this;
        }
    }
}

namespace Siemens.Engineering.HW
{
    public abstract class Software : NamedObject { }
}

namespace Siemens.Engineering.Hmi
{
    // The Classic runtime type is recognised by FullName only; the worker never references this assembly.
    public sealed class HmiTarget : HW.Software { }
}

namespace Siemens.Engineering.HmiUnified
{
    public sealed class HmiSoftware : HW.Software
    {
        public Scripts.HmiScriptModuleComposition Scripts { get; } = new();
        public TextGraphicList.HmiTextListComposition HmiTextLists { get; } = new();
        public TextGraphicList.HmiGraphicListComposition HmiGraphicLists { get; } = new();
        public TextGraphicList.HmiSystemTextListComposition HmiSystemTextLists { get; } = new();
        public Exception? RuntimeSettingsFailure { get; set; }
        private RuntimeSettings.HmiRuntimeSetting runtimeSettings = new();
        public RuntimeSettings.HmiRuntimeSetting RuntimeSettings
        {
            get => RuntimeSettingsFailure is null ? runtimeSettings : throw RuntimeSettingsFailure;
            set => runtimeSettings = value;
        }
    }
}

namespace Siemens.Engineering.HmiUnified.Scripts
{
    public sealed class HmiScriptModule : NamedObject { }
    public sealed class HmiScriptModuleComposition : Composition<HmiScriptModule> { }
}

namespace Siemens.Engineering.HmiUnified.TextGraphicList
{
    public sealed class HmiTextList : NamedObject { }
    public sealed class HmiTextListComposition : Composition<HmiTextList> { }
    public sealed class HmiGraphicList : NamedObject { }
    public sealed class HmiGraphicListComposition : Composition<HmiGraphicList> { }
    public sealed class HmiSystemTextList : NamedObject { }
    public sealed class HmiSystemTextListComposition : Composition<HmiSystemTextList> { }
}

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
    using Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeSettingsCommon;

    /// <summary>Property bag: <c>Failures[name]</c> makes that property throw, as Openness does on unsupported devices.</summary>
    public abstract class SettingsBag
    {
        private readonly Dictionary<string, object?> values = new();
        public Dictionary<string, Exception> Failures { get; } = new();
        protected T Get<T>([CallerMemberName] string name = "")
        {
            if (Failures.TryGetValue(name, out var failure)) throw failure;
            return values.TryGetValue(name, out var v) ? (T)v! : default!;
        }
        protected void Set(object? value, [CallerMemberName] string name = "") => values[name] = value;
    }

    public sealed class HmiRuntimeSetting : SettingsBag
    {
        public string AutoLogOffURL { get => Get<string>(); set => Set(value); }
        public bool BitSelection { get => Get<bool>(); set => Set(value); }
        public BitNumberEvaluationType BitSelectionStrategyForResourceLists { get => Get<BitNumberEvaluationType>(); set => Set(value); }
        public BitNumberEvaluationType BitSelectionStrategyForTagDynamization { get => Get<BitNumberEvaluationType>(); set => Set(value); }
        public CentralInteractionSetting CentralInputHint { get => Get<CentralInteractionSetting>(); set => Set(value); }
        public CentralInteractionSetting CentralPanning { get => Get<CentralInteractionSetting>(); set => Set(value); }
        public CentralInteractionSetting CentralZooming { get => Get<CentralInteractionSetting>(); set => Set(value); }
        public bool EnableLanguageCompatibleFontFamilies { get => Get<bool>(); set => Set(value); }
        public bool GMPEnabled { get => Get<bool>(); set => Set(value); }
        public GeneralESIGCommentsStrategy GeneralESIGCommentsStrategy { get => Get<GeneralESIGCommentsStrategy>(); set => Set(value); }
        public HmiExclusiveOperationSettings HmiExclusiveOperationSettings { get => Get<HmiExclusiveOperationSettings>(); set => Set(value); }
        public HmiReportingSettings HmiReportingSettings { get => Get<HmiReportingSettings>(); set => Set(value); }
        public HmiUnifiedTagSettings HmiUnifiedTagSettings { get => Get<HmiUnifiedTagSettings>(); set => Set(value); }
        public HmiUpssRuntimeSettings HmiUpssRuntimeSettings { get => Get<HmiUpssRuntimeSettings>(); set => Set(value); }
        public HmiLanguageAndFontAssociation LanguageAndFonts { get; } = new();
        public HmiMaxLoginRuntimeSettings MaxLoginRuntimeSettings { get => Get<HmiMaxLoginRuntimeSettings>(); set => Set(value); }
        public HmiOpcUaServerRuntimeSettings OpcUaServerRuntimeSettings { get => Get<HmiOpcUaServerRuntimeSettings>(); set => Set(value); }
        public HmiProcessDiagnosticsRuntimeSettings ProcessDiagnosticsRuntimeSettings { get => Get<HmiProcessDiagnosticsRuntimeSettings>(); set => Set(value); }
        public HmiRuntimeResourceSettings RuntimeResourceSettings { get => Get<HmiRuntimeResourceSettings>(); set => Set(value); }
        public ScreenResolution ScreenResolution { get => Get<ScreenResolution>(); set => Set(value); }
        public string StartScreen { get => Get<string>(); set => Set(value); }
        public HmiTelemetryRuntimeSettings TelemetryRuntimeSettings { get => Get<HmiTelemetryRuntimeSettings>(); set => Set(value); }
    }

    public sealed class HmiExclusiveOperationSettings : SettingsBag
    {
        public string ExclusiveOperationControlTag { get => Get<string>(); set => Set(value); }
        public string ExclusiveOperationStatusTag { get => Get<string>(); set => Set(value); }
    }

    public sealed class HmiReportingSettings : SettingsBag
    {
        public bool IsReportingEnabled { get => Get<bool>(); set => Set(value); }
        public StorageLocation ReportingDatabaseStorage { get => Get<StorageLocation>(); set => Set(value); }
        public string ReportingDatabaseStoragePath { get => Get<string>(); set => Set(value); }
        public StorageLocation ReportingMainStorage { get => Get<StorageLocation>(); set => Set(value); }
        public string ReportingMainStoragePath { get => Get<string>(); set => Set(value); }
    }

    public sealed class HmiUnifiedTagSettings : SettingsBag
    {
        public bool IgnoreInitialQCNotifications { get => Get<bool>(); set => Set(value); }
        public bool IgnoreTimestampNotifications { get => Get<bool>(); set => Set(value); }
        public bool TagOptimizationActive { get => Get<bool>(); set => Set(value); }
    }

    public sealed class HmiUpssRuntimeSettings : SettingsBag
    {
        public string GlobalScopePersistencyAuthorization { get => Get<string>(); set => Set(value); }
        public GeneralPersistencyStrategy PersistencyStrategy { get => Get<GeneralPersistencyStrategy>(); set => Set(value); }
    }

    public sealed class HmiMaxLoginRuntimeSettings : SettingsBag
    {
        public bool EnableLockAfterNumberOfAttempts { get => Get<bool>(); set => Set(value); }
        public uint MaxLoginErrors { get => Get<uint>(); set => Set(value); }
    }

    public sealed class HmiProcessDiagnosticsRuntimeSettings : SettingsBag
    {
        public bool CriteriaAnalysisAbsoluteAddress { get => Get<bool>(); set => Set(value); }
        public ExtendTextWith CriteriaAnalysisAll { get => Get<ExtendTextWith>(); set => Set(value); }
        public bool CriteriaAnalysisComment { get => Get<bool>(); set => Set(value); }
        public CriteriaAnalysisExtendedText CriteriaAnalysisExtendText { get => Get<CriteriaAnalysisExtendedText>(); set => Set(value); }
        public bool CriteriaAnalysisSymbol { get => Get<bool>(); set => Set(value); }
        public bool CriteriaAnalysisValue { get => Get<bool>(); set => Set(value); }
        public bool EnableProcessDiagnostics { get => Get<bool>(); set => Set(value); }
    }

    public sealed class HmiRuntimeResourceSettings : SettingsBag
    {
        public bool EnableHighResolutionGraphicsOptimization { get => Get<bool>(); set => Set(value); }
    }

    public sealed class HmiTelemetryRuntimeSettings : SettingsBag
    {
        public bool TelemetryActive { get => Get<bool>(); set => Set(value); }
        public string TelemetryStorageFolder { get => Get<string>(); set => Set(value); }
        public StorageLocation TelemetryStorageMedia { get => Get<StorageLocation>(); set => Set(value); }
    }

    public sealed class HmiOpcUaServerRuntimeSettings : SettingsBag
    {
        public bool ActAsOPCServer { get => Get<bool>(); set => Set(value); }
        public int EndpointUrlPortNumber { get => Get<int>(); set => Set(value); }
        public bool EnableGuestAuthentication { get => Get<bool>(); set => Set(value); }
        public bool EnableUsernameAndPasswordAuthentication { get => Get<bool>(); set => Set(value); }
        public int MaxSessionCount { get => Get<int>(); set => Set(value); }
        public bool OpcUaServerSecurityNone { get => Get<bool>(); set => Set(value); }
    }

    public sealed class HmiLanguageAndFont : SettingsBag
    {
        public string Language { get => Get<string>(); set => Set(value); }
        public short Order { get => Get<short>(); set => Set(value); }
        public bool Enable { get => Get<bool>(); set => Set(value); }
        public bool EnableForLogging { get => Get<bool>(); set => Set(value); }
        public string DefaultFont { get => Get<string>(); set => Set(value); }
        public string FixedFont1 { get => Get<string>(); set => Set(value); }
        public string FixedFont2 { get => Get<string>(); set => Set(value); }
        public string FixedFont3 { get => Get<string>(); set => Set(value); }
        public string FixedFont4 { get => Get<string>(); set => Set(value); }
    }

    public sealed class HmiLanguageAndFontAssociation : IEnumerable<HmiLanguageAndFont>
    {
        public List<HmiLanguageAndFont> Items { get; } = new();
        public Exception? EnumerationFailure { get; set; }
        public IEnumerator<HmiLanguageAndFont> GetEnumerator()
        {
            if (EnumerationFailure is not null) throw EnumerationFailure;
            return Items.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
