using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Hmi;

/// <summary>Strict request shape for one hmi_read operation; the catalog restricts fields per operation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HmiOperationRequest : IOperationBatchItem
{
    [Description("Client-supplied unique identifier for this HMI operation; returned results are keyed by it.")]
    public string OperationId { get; set; } = string.Empty;

    [Description("HMI operation to run: list_hmi_devices, list_tag_tables, list_tags, get_tag, list_system_tags, list_connections, list_alarms, get_alarm, list_alarm_classes, list_logs, list_logging_tags, list_screens, list_screen_items, list_faceplate_instances, get_screen_navigation, get_runtime_settings, list_script_modules, list_text_and_graphic_lists, list_project_languages, validate.")]
    public string Operation { get; set; } = string.Empty;

    [Description("Optional absolute .ap21 project or .amc21 local-session engineering path. Reads never open or switch a project; a path that differs from the bound project fails only this item with binding_conflict.")]
    public string? ProjectPath { get; set; }

    [Description("Optional WinCC Unified HMI, matched case-insensitively on software name or device name. May be omitted only when the project has exactly one Unified HMI, otherwise the item fails with target_ambiguous. Not valid for list_hmi_devices and list_project_languages.")]
    public string? HmiName { get; set; }

    [Description("Optional culture name such as en-US; narrows every text array of the result to that culture. A culture that is not a project language fails the item with target_not_found.")]
    public string? Language { get; set; }

    [Description("Paged operations only: zero-based index of the first row. Default 0.")]
    public int? Offset { get; set; }

    [Description("Paged operations only: rows per page, 1 to 2000. Default 100.")]
    public int? Limit { get; set; }

    [Description("Optional slash-separated group names below the root collection (exact, case-sensitive), for list_tag_tables and list_screens.")]
    public string? GroupPath { get; set; }

    [Description("Tag table name. Optional for list_tags.")]
    public string? TableName { get; set; }

    [Description("Tag name. Required by get_tag; optional filter for list_logging_tags.")]
    public string? TagName { get; set; }

    [Description("Data log name. Optional filter for list_logging_tags.")]
    public string? DataLogName { get; set; }

    [Description("Alarm name. Required by get_alarm.")]
    public string? AlarmName { get; set; }

    [Description("Alarm kind: discrete or analog. Required by get_alarm; optional filter for list_alarms.")]
    public string? AlarmKind { get; set; }

    [Description("Screen name. Required by list_screen_items; optional filter for list_faceplate_instances.")]
    public string? ScreenName { get; set; }

    [Description("validate category: tags, alarms, screens, connections, logs, alarmClasses or all. Required by validate.")]
    public string? Category { get; set; }

    [Description("validate: narrows to one object of the given category; not valid with category all.")]
    public string? Name { get; set; }
}
