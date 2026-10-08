namespace TiaMcpServer.Contracts;

/// <summary>
/// Result of <c>get_runtime_settings</c>. Sub-objects that a device does not have (for example reporting
/// on a Unified Comfort panel) are null with one entry in <see cref="Messages"/>. A property that could
/// not be read is a null value, one message and <see cref="IsComplete"/> false. All values are strings:
/// booleans are <c>true</c>/<c>false</c>, enums are their names, numbers use the invariant culture.
/// </summary>
public class HmiRuntimeSettingsInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public HmiRuntimeSectionInfo General { get; set; } = new HmiRuntimeSectionInfo();

    public List<HmiLanguageAndFontInfo> LanguageAndFonts { get; set; } = new List<HmiLanguageAndFontInfo>();

    public HmiRuntimeSectionInfo? ExclusiveOperation { get; set; }

    public HmiRuntimeSectionInfo? UnifiedTags { get; set; }

    public HmiRuntimeSectionInfo? Reporting { get; set; }

    public HmiRuntimeSectionInfo? Upss { get; set; }

    public HmiRuntimeSectionInfo? MaxLogin { get; set; }

    public HmiRuntimeSectionInfo? ProcessDiagnostics { get; set; }

    public HmiRuntimeSectionInfo? RuntimeResources { get; set; }

    public HmiRuntimeSectionInfo? Telemetry { get; set; }

    /// <summary>A subset of the section's settings (port, session limits, authentication, security none).</summary>
    public HmiRuntimeSectionInfo? OpcUaServer { get; set; }
}

public class HmiRuntimeSectionInfo
{
    public List<HmiSettingValue> Values { get; set; } = new List<HmiSettingValue>();
}

public class HmiSettingValue
{
    public string Name { get; set; } = string.Empty;

    public string? Value { get; set; }
}

public class HmiLanguageAndFontInfo
{
    public string? Language { get; set; }

    public int? Order { get; set; }

    public bool? Enable { get; set; }

    public bool? EnableForLogging { get; set; }

    public string? DefaultFont { get; set; }

    public string? FixedFont1 { get; set; }

    public string? FixedFont2 { get; set; }

    public string? FixedFont3 { get; set; }

    public string? FixedFont4 { get; set; }
}
