using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Pure read of the extended project-metadata surface served by <c>get_project_status</c>:
/// copyright, family, the multilingual project comment, language settings, history entries,
/// used products, and the V21 block-compilation toggles.
///
/// Read-only by construction: nothing here opens, closes, switches, saves, or sets any value,
/// and it never mutates TIA Portal state. Each section is read independently; a section that
/// throws <see cref="EngineeringException"/> degrades to a warning and null/empty output (never a
/// fabricated default), while any unrelated exception propagates to the normal worker failure
/// path. History is deterministically capped to <see cref="MaxHistoryEntries"/> with an explicit
/// <see cref="ProjectMetadataInfo.HistoryTruncated"/> flag rather than silently dropping entries.
/// </summary>
public static class ProjectMetadataReader
{
    /// <summary>
    /// Deterministic cap on how many history entries <c>get_project_status</c> exposes. TIA
    /// Portal keeps the full history; the worker preserves the oldest entries in order and sets
    /// <see cref="ProjectMetadataInfo.HistoryTruncated"/> when Openness reported more than this.
    /// </summary>
    public const int MaxHistoryEntries = 1000;

    public static ProjectMetadataInfo Read(Project project)
    {
        return new ProjectMetadataInfo
        {
            Copyright = ReadString(() => project.Copyright, "copyright"),
            Family = ReadString(() => project.Family, "family"),
            Comment = ReadComment(project),
            LanguageSettings = ReadLanguageSettings(project),
            HistoryEntries = ReadHistory(project, out var truncated),
            HistoryTruncated = truncated,
            UsedProducts = ReadUsedProducts(project),
            CompilationSettings = ReadCompilationSettings(project)
        };
    }

    private static ProjectCommentInfo? ReadComment(Project project)
    {
        try
        {
            var items = project.Comment?.Items;
            var translations = new List<ProjectCommentTranslationInfo>();
            if (items is not null)
            {
                foreach (var item in items)
                {
                    translations.Add(new ProjectCommentTranslationInfo
                    {
                        Culture = item.Language?.Culture?.Name,
                        Text = item.Text
                    });
                }
            }

            return new ProjectCommentInfo { Translations = translations };
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Could not read project comment: {ex.Message}");
            return null;
        }
    }

    private static ProjectLanguageSettingsInfo? ReadLanguageSettings(Project project)
    {
        try
        {
            var languageSettings = project.LanguageSettings;
            if (languageSettings is null)
            {
                return null;
            }

            return new ProjectLanguageSettingsInfo
            {
                Languages = CultureNames(languageSettings.Languages),
                ActiveLanguages = CultureNames(languageSettings.ActiveLanguages),
                EditingLanguage = CultureOf(languageSettings.EditingLanguage),
                ReferenceLanguage = CultureOf(languageSettings.ReferenceLanguage)
            };
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Could not read project language settings: {ex.Message}");
            return null;
        }
    }

    private static List<ProjectHistoryEntryInfo>? ReadHistory(Project project, out bool? truncated)
    {
        truncated = false;
        try
        {
            var entries = new List<ProjectHistoryEntryInfo>();
            foreach (var entry in project.HistoryEntries)
            {
                if (entries.Count >= MaxHistoryEntries)
                {
                    truncated = true;
                    break;
                }

                entries.Add(new ProjectHistoryEntryInfo
                {
                    Text = entry.Text,
                    DateTime = entry.DateTime
                });
            }

            return entries;
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Could not read project history: {ex.Message}");
            truncated = null;
            return null;
        }
    }

    private static List<ProjectUsedProductInfo>? ReadUsedProducts(Project project)
    {
        try
        {
            var products = new List<ProjectUsedProductInfo>();
            foreach (var product in project.UsedProducts)
            {
                products.Add(new ProjectUsedProductInfo
                {
                    Name = product.Name,
                    Version = product.Version
                });
            }

            return products;
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Could not read used products: {ex.Message}");
            return null;
        }
    }

    private static ProjectCompilationSettingsInfo? ReadCompilationSettings(Project project)
    {
        var result = new ProjectCompilationSettingsInfo();

        result.IsSimulationDuringBlockCompilationEnabled = ReadProviderSetting(
            "PlcSimulationSettingsProvider",
            "IsSimulationDuringBlockCompilationEnabled",
            () => project.GetService<PlcSimulationSettingsProvider>()?.IsSimulationDuringBlockCompilationEnabled);

        result.IsVirtualPlcDuringBlockCompilationEnabled = ReadProviderSetting(
            "VirtualPlcSettingsProvider",
            "IsVirtualPlcDuringBlockCompilationEnabled",
            () => project.GetService<VirtualPlcSettingsProvider>()?.IsVirtualPlcDuringBlockCompilationEnabled);

        // Both values null with no provider available is a genuine "nothing to report" state;
        // but the section is still returned (empty) so the caller sees the schema, and each
        // reader function already warned exactly why any given value is absent.
        return result;
    }

    private static bool? ReadProviderSetting(string providerName, string settingName, Func<bool?> read)
    {
        try
        {
            var value = read();
            if (value is null)
            {
                Console.Error.WriteLine(
                    $"{providerName} was unavailable; {settingName} is not reported.");
            }

            return value;
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine(
                $"Could not read {providerName}.{settingName}: {ex.Message}");
            return null;
        }
    }

    private static string? ReadString(Func<string> read, string fieldName)
    {
        try
        {
            return read();
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Could not read project {fieldName}: {ex.Message}");
            return null;
        }
    }

    private static List<string>? CultureNames(IEnumerable<Language> languages)
    {
        // Openness yields a live collection; snapshot it so a single section read either fully
        // succeeds or degrades as a whole rather than racing an EngineeringException mid-iteration.
        var names = new List<string>();
        foreach (var language in languages)
        {
            var culture = CultureOf(language);
            if (culture is not null)
            {
                names.Add(culture);
            }
        }

        return names;
    }

    private static string? CultureOf(Language? language)
    {
        if (language is null)
        {
            return null;
        }

        try
        {
            return language.Culture?.Name;
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Could not read language culture: {ex.Message}");
            return null;
        }
    }
}