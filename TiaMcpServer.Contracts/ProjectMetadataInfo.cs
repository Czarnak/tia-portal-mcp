using System;
using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Extended read-only project metadata, attached to <see cref="ProjectStatusInfo.Metadata"/> for
/// the direct <c>get_project_status</c> read. Every member here comes straight from TIA Portal
/// Openness; nothing is fabricated, translated, summarized, or defaulted when unreadable (such a
/// value is left null/empty and the worker emits a warning). All collections preserve source
/// order and expose every entry Openness reports.
/// </summary>
public class ProjectMetadataInfo
{
    public string? Copyright { get; set; }

    public string? Family { get; set; }

    public ProjectCommentInfo? Comment { get; set; }

    public ProjectLanguageSettingsInfo? LanguageSettings { get; set; }

    public List<ProjectHistoryEntryInfo>? HistoryEntries { get; set; }

    /// <summary>
    /// True only when Openness reported more history entries than the reader exposes; the
    /// oldest entries are kept in order and the response carries this flag instead of silently
    /// hiding the remainder.
    /// </summary>
    public bool HistoryTruncated { get; set; }

    public List<ProjectUsedProductInfo>? UsedProducts { get; set; }

    /// <summary>
    /// Block-compilation toggle state read through the V21
    /// <c>Siemens.Engineering.SW.PlcSimulationSettingsProvider</c> / <c>VirtualPlcSettingsProvider</c>
    /// services. A member is <c>null</c> when the service or its value could not be read
    /// (reported as a warning), never a fabricated default.
    /// </summary>
    public ProjectCompilationSettingsInfo? CompilationSettings { get; set; }
}

/// <summary>All Openness-reported multilingual comment translations, in source order.</summary>
public class ProjectCommentInfo
{
    public List<ProjectCommentTranslationInfo>? Translations { get; set; }
}

/// <summary>One multilingual comment translation: history culture and its text, exactly as reported.</summary>
public class ProjectCommentTranslationInfo
{
    /// <summary>Language culture name (for example <c>en-US</c>), exactly as Openness reports it.</summary>
    public string? Culture { get; set; }

    public string? Text { get; set; }
}

public class ProjectLanguageSettingsInfo
{
    /// <summary>Every language registered in the project, in source order.</summary>
    public List<string>? Languages { get; set; }

    /// <summary>Languages currently active for the project, in source order.</summary>
    public List<string>? ActiveLanguages { get; set; }

    public string? EditingLanguage { get; set; }

    public string? ReferenceLanguage { get; set; }
}

/// <summary>A single project history entry; text is exposed verbatim in the order Openness yields it.</summary>
public class ProjectHistoryEntryInfo
{
    public string? Text { get; set; }

    public DateTime? DateTime { get; set; }
}

/// <summary>A product recorded as used by the project. No inference and no silent deduplication.</summary>
public class ProjectUsedProductInfo
{
    public string? Name { get; set; }

    public string? Version { get; set; }
}

/// <summary>
/// Block-compilation toggles from the V21 settings services. Values are <c>null</c> when the
/// corresponding provider or value was unavailable (warned), never hard-set to <c>false</c>.
/// </summary>
public class ProjectCompilationSettingsInfo
{
    public bool? IsSimulationDuringBlockCompilationEnabled { get; set; }

    public bool? IsVirtualPlcDuringBlockCompilationEnabled { get; set; }
}