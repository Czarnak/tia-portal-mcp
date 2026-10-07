using System.Globalization;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.RuntimeSettings;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>Operations 1 and 16-19: device discovery and software-level information.</summary>
public static class HmiSoftwareInfoReader
{
    public static HmiDeviceListInfo ListDevices(Project project)
    {
        var messages = new List<string>();
        var entries = HmiSoftwareLocator.EnumerateAll(project, messages).ToList();
        var ordered = HmiPager.InNameOrder(
            HmiPager.InNameOrder(entries, e => e.SoftwareName), e => e.DeviceName).ToList();

        return new HmiDeviceListInfo
        {
            IsComplete = messages.Count == 0,
            Messages = messages,
            Devices = ordered
                .Select(e => new HmiDeviceInfo
                {
                    DeviceName = e.DeviceName,
                    SoftwareName = e.SoftwareName,
                    Kind = e.Kind,
                    TypeIdentifier = e.TypeIdentifier,
                })
                .ToList(),
        };
    }

    public static HmiRuntimeSettingsInfo ReadRuntimeSettings(HmiSoftware software)
    {
        HmiRuntimeSetting settings;
        try
        {
            settings = software.RuntimeSettings
                ?? throw new WorkerOperationException(
                    WorkerFailureCategories.WorkerOperationFailed, "The runtime settings of the HMI are not available.");
        }
        catch (EngineeringException ex)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed, $"The runtime settings could not be read: {ex.Message}");
        }

        var log = new HmiReadLog();
        var info = new HmiRuntimeSettingsInfo
        {
            General = Section(log, new (string, Func<object?>)[]
            {
                ("AutoLogOffURL", () => settings.AutoLogOffURL),
                ("BitSelection", () => settings.BitSelection),
                ("BitSelectionStrategyForResourceLists", () => settings.BitSelectionStrategyForResourceLists),
                ("BitSelectionStrategyForTagDynamization", () => settings.BitSelectionStrategyForTagDynamization),
                ("CentralInputHint", () => settings.CentralInputHint),
                ("CentralPanning", () => settings.CentralPanning),
                ("CentralZooming", () => settings.CentralZooming),
                ("EnableLanguageCompatibleFontFamilies", () => settings.EnableLanguageCompatibleFontFamilies),
                ("GMPEnabled", () => settings.GMPEnabled),
                ("GeneralESIGCommentsStrategy", () => settings.GeneralESIGCommentsStrategy),
                ("ScreenResolution", () => settings.ScreenResolution),
                ("StartScreen", () => settings.StartScreen),
            }),
            LanguageAndFonts = ReadLanguageAndFonts(settings, log),
            ExclusiveOperation = SubSection(log, "HmiExclusiveOperationSettings", () => settings.HmiExclusiveOperationSettings,
                s => new (string, Func<object?>)[]
                {
                    ("ExclusiveOperationControlTag", () => s.ExclusiveOperationControlTag),
                    ("ExclusiveOperationStatusTag", () => s.ExclusiveOperationStatusTag),
                }),
            UnifiedTags = SubSection(log, "HmiUnifiedTagSettings", () => settings.HmiUnifiedTagSettings,
                s => new (string, Func<object?>)[]
                {
                    ("IgnoreInitialQCNotifications", () => s.IgnoreInitialQCNotifications),
                    ("IgnoreTimestampNotifications", () => s.IgnoreTimestampNotifications),
                    ("TagOptimizationActive", () => s.TagOptimizationActive),
                }),
            Reporting = SubSection(log, "HmiReportingSettings", () => settings.HmiReportingSettings,
                s => new (string, Func<object?>)[]
                {
                    ("IsReportingEnabled", () => s.IsReportingEnabled),
                    ("ReportingDatabaseStorage", () => s.ReportingDatabaseStorage),
                    ("ReportingDatabaseStoragePath", () => s.ReportingDatabaseStoragePath),
                    ("ReportingMainStorage", () => s.ReportingMainStorage),
                    ("ReportingMainStoragePath", () => s.ReportingMainStoragePath),
                }),
            Upss = SubSection(log, "HmiUpssRuntimeSettings", () => settings.HmiUpssRuntimeSettings,
                s => new (string, Func<object?>)[]
                {
                    ("GlobalScopePersistencyAuthorization", () => s.GlobalScopePersistencyAuthorization),
                    ("PersistencyStrategy", () => s.PersistencyStrategy),
                }),
            MaxLogin = SubSection(log, "MaxLoginRuntimeSettings", () => settings.MaxLoginRuntimeSettings,
                s => new (string, Func<object?>)[]
                {
                    ("EnableLockAfterNumberOfAttempts", () => s.EnableLockAfterNumberOfAttempts),
                    ("MaxLoginErrors", () => s.MaxLoginErrors),
                }),
            ProcessDiagnostics = SubSection(log, "ProcessDiagnosticsRuntimeSettings", () => settings.ProcessDiagnosticsRuntimeSettings,
                s => new (string, Func<object?>)[]
                {
                    ("CriteriaAnalysisAbsoluteAddress", () => s.CriteriaAnalysisAbsoluteAddress),
                    ("CriteriaAnalysisAll", () => s.CriteriaAnalysisAll),
                    ("CriteriaAnalysisComment", () => s.CriteriaAnalysisComment),
                    ("CriteriaAnalysisExtendText", () => s.CriteriaAnalysisExtendText),
                    ("CriteriaAnalysisSymbol", () => s.CriteriaAnalysisSymbol),
                    ("CriteriaAnalysisValue", () => s.CriteriaAnalysisValue),
                    ("EnableProcessDiagnostics", () => s.EnableProcessDiagnostics),
                }),
            RuntimeResources = SubSection(log, "RuntimeResourceSettings", () => settings.RuntimeResourceSettings,
                s => new (string, Func<object?>)[]
                {
                    ("EnableHighResolutionGraphicsOptimization", () => s.EnableHighResolutionGraphicsOptimization),
                }),
            Telemetry = SubSection(log, "TelemetryRuntimeSettings", () => settings.TelemetryRuntimeSettings,
                s => new (string, Func<object?>)[]
                {
                    ("TelemetryActive", () => s.TelemetryActive),
                    ("TelemetryStorageFolder", () => s.TelemetryStorageFolder),
                    ("TelemetryStorageMedia", () => s.TelemetryStorageMedia),
                }),
            OpcUaServer = SubSection(log, "OpcUaServerRuntimeSettings", () => settings.OpcUaServerRuntimeSettings,
                s => new (string, Func<object?>)[]
                {
                    ("ActAsOPCServer", () => s.ActAsOPCServer),
                    ("EndpointUrlPortNumber", () => s.EndpointUrlPortNumber),
                    ("EnableGuestAuthentication", () => s.EnableGuestAuthentication),
                    ("EnableUsernameAndPasswordAuthentication", () => s.EnableUsernameAndPasswordAuthentication),
                    ("MaxSessionCount", () => s.MaxSessionCount),
                    ("OpcUaServerSecurityNone", () => s.OpcUaServerSecurityNone),
                }),
        };

        info.IsComplete = log.IsComplete;
        info.Messages = log.Messages;
        return info;
    }

    public static HmiScriptModuleListInfo ListScriptModules(HmiSoftware software)
    {
        var log = new HmiReadLog();
        var modules = ReadNames(() => software.Scripts, m => m.Name, "script modules", log);
        return new HmiScriptModuleListInfo { IsComplete = log.IsComplete, Messages = log.Messages, Modules = modules };
    }

    public static HmiTextAndGraphicListsInfo ListTextAndGraphicLists(HmiSoftware software)
    {
        var log = new HmiReadLog();
        return new HmiTextAndGraphicListsInfo
        {
            TextLists = ReadNames(() => software.HmiTextLists, l => l.Name, "text lists", log),
            GraphicLists = ReadNames(() => software.HmiGraphicLists, l => l.Name, "graphic lists", log),
            SystemTextLists = ReadNames(() => software.HmiSystemTextLists, l => l.Name, "system text lists", log),
            IsComplete = log.IsComplete,
            Messages = log.Messages,
        };
    }

    public static HmiProjectLanguagesInfo ListProjectLanguages(Project project)
    {
        var log = new HmiReadLog();
        var languages = HmiTextMapper.ProjectCultures(project).ToList();
        return new HmiProjectLanguagesInfo
        {
            Languages = languages,
            EditingLanguage = log.Try(() => project.LanguageSettings.EditingLanguage.Culture.Name, "The editing language"),
            ReferenceLanguage = log.Try(() => project.LanguageSettings.ReferenceLanguage.Culture.Name, "The reference language"),
            IsComplete = log.IsComplete,
            Messages = log.Messages,
        };
    }

    /// <summary>Names of a composition, ordered by name. An unreadable composition fails the item; an unreadable name is skipped with a message.</summary>
    private static List<string> ReadNames<TItem>(
        Func<IEnumerable<TItem>> composition, Func<TItem, string> name, string what, HmiReadLog log)
    {
        var names = new List<string>();
        try
        {
            foreach (var item in composition())
            {
                var itemName = log.Try(() => name(item), $"The name of a {what.TrimEnd('s')} entry");
                if (itemName is not null)
                {
                    names.Add(itemName);
                }
            }
        }
        catch (EngineeringException ex)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed, $"The {what} could not be read: {ex.Message}");
        }

        return HmiPager.InNameOrder(names, n => n).ToList();
    }

    private static List<HmiLanguageAndFontInfo> ReadLanguageAndFonts(HmiRuntimeSetting settings, HmiReadLog log)
    {
        var entries = new List<HmiLanguageAndFontInfo>();
        try
        {
            foreach (var entry in settings.LanguageAndFonts)
            {
                entries.Add(new HmiLanguageAndFontInfo
                {
                    Language = log.Try(() => entry.Language, "LanguageAndFonts.Language"),
                    Order = log.Try<int?>(() => entry.Order, "LanguageAndFonts.Order"),
                    Enable = log.Try<bool?>(() => entry.Enable, "LanguageAndFonts.Enable"),
                    EnableForLogging = log.Try<bool?>(() => entry.EnableForLogging, "LanguageAndFonts.EnableForLogging"),
                    DefaultFont = log.Try(() => entry.DefaultFont, "LanguageAndFonts.DefaultFont"),
                    FixedFont1 = log.Try(() => entry.FixedFont1, "LanguageAndFonts.FixedFont1"),
                    FixedFont2 = log.Try(() => entry.FixedFont2, "LanguageAndFonts.FixedFont2"),
                    FixedFont3 = log.Try(() => entry.FixedFont3, "LanguageAndFonts.FixedFont3"),
                    FixedFont4 = log.Try(() => entry.FixedFont4, "LanguageAndFonts.FixedFont4"),
                });
            }
        }
        catch (EngineeringException ex)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed, $"The LanguageAndFonts could not be read: {ex.Message}");
        }

        return entries
            .OrderBy(e => e.Order ?? int.MaxValue)
            .ThenBy(e => e.Language, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static HmiRuntimeSectionInfo Section(HmiReadLog log, (string Name, Func<object?> Read)[] properties)
    {
        var section = new HmiRuntimeSectionInfo();
        foreach (var (name, read) in properties)
        {
            section.Values.Add(new HmiSettingValue { Name = name, Value = Format(log.Try(read, $"Runtime setting '{name}'")) });
        }

        return section;
    }

    /// <summary>A device-dependent sub-object: null (with a note) when the device does not have it, null (with a failure) when reading it throws.</summary>
    private static HmiRuntimeSectionInfo? SubSection<T>(
        HmiReadLog log, string name, Func<T?> get, Func<T, (string Name, Func<object?> Read)[]> properties)
        where T : class
    {
        T? section;
        try
        {
            section = get();
        }
        catch (EngineeringException ex)
        {
            log.Recover(ex, $"{name} could not be read: {ex.Message}");
            return null;
        }

        if (section is null)
        {
            log.Note($"{name} is not available on this device.");
            return null;
        }

        return Section(log, properties(section));
    }

    private static string? Format(object? value) => value switch
    {
        null => null,
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
