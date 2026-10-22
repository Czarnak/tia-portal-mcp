namespace TiaMcpServer.Contracts.Hmi;

public static class HmiReadMethods
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        "hmi_list_hmi_devices",
        "hmi_list_tag_tables",
        "hmi_list_tags",
        "hmi_get_tag",
        "hmi_list_system_tags",
        "hmi_list_connections",
        "hmi_list_alarms",
        "hmi_get_alarm",
        "hmi_list_alarm_classes",
        "hmi_list_logs",
        "hmi_list_logging_tags",
        "hmi_list_screens",
        "hmi_list_screen_items",
        "hmi_list_faceplate_instances",
        "hmi_get_screen_navigation",
        "hmi_get_runtime_settings",
        "hmi_list_script_modules",
        "hmi_list_text_and_graphic_lists",
        "hmi_list_project_languages",
        "hmi_validate",
    });
}
