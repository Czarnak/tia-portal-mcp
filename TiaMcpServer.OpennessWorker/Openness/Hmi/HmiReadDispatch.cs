using Siemens.Engineering;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>Routes an <c>hmi_*</c> worker method to its reader. Unknown methods are denied; reads never bind, switch or open a project.</summary>
public static class HmiReadDispatch
{
    public static object Read(Project project, string method, HmiQueryInfo query)
    {
        if (query.Language is not null)
        {
            HmiTextMapper.RequireProjectLanguage(project, query.Language);
        }

        return method switch
        {
            "hmi_list_hmi_devices" => HmiSoftwareInfoReader.ListDevices(project),
            "hmi_get_runtime_settings" => HmiSoftwareInfoReader.ReadRuntimeSettings(Software(project, query)),
            "hmi_list_script_modules" => HmiSoftwareInfoReader.ListScriptModules(Software(project, query)),
            "hmi_list_text_and_graphic_lists" => HmiSoftwareInfoReader.ListTextAndGraphicLists(Software(project, query)),
            "hmi_list_project_languages" => HmiSoftwareInfoReader.ListProjectLanguages(project),
            "hmi_list_tag_tables" => HmiTagReader.ListTagTables(Software(project, query), query.GroupPath),
            "hmi_list_tags" => HmiTagReader.ListTags(
                Software(project, query), query.TableName, query.Language, Offset(query), Limit(query)),
            "hmi_get_tag" => HmiTagReader.GetTag(
                Software(project, query), query.TagName ?? throw MissingField("tagName"), query.Language),
            "hmi_list_system_tags" => HmiTagReader.ListSystemTags(Software(project, query), Offset(query), Limit(query)),
            "hmi_list_logging_tags" => HmiTagReader.ListLoggingTags(
                Software(project, query), query.DataLogName, query.TagName, Offset(query), Limit(query)),
            "hmi_list_connections" => HmiConnectionReader.ListConnections(Software(project, query)),
            "hmi_list_alarms" => HmiAlarmReader.ListAlarms(
                Software(project, query), query.AlarmKind, query.Language, Offset(query), Limit(query)),
            "hmi_get_alarm" => HmiAlarmReader.GetAlarm(
                Software(project, query),
                query.AlarmName ?? throw MissingField("alarmName"),
                query.AlarmKind ?? throw MissingField("alarmKind"),
                query.Language),
            "hmi_list_alarm_classes" => HmiAlarmReader.ListAlarmClasses(Software(project, query)),
            "hmi_list_logs" => HmiLogReader.ListLogs(Software(project, query)),
            "hmi_list_screens" => HmiScreenReader.ListScreens(Software(project, query), query.GroupPath, query.Language),
            "hmi_list_screen_items" => HmiScreenReader.ListScreenItems(
                Software(project, query), query.ScreenName ?? throw MissingField("screenName"), Offset(query), Limit(query)),
            "hmi_list_faceplate_instances" => HmiScreenReader.ListFaceplateInstances(
                Software(project, query), query.ScreenName, Offset(query), Limit(query)),
            "hmi_get_screen_navigation" => HmiScreenReader.GetScreenNavigation(Software(project, query)),
            _ => throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError, $"Unsupported HMI worker method '{method}'."),
        };
    }

    // The host always sends concrete paging values for paged operations (HmiOperationCatalog.DefaultLimit); these only guard a malformed request.
    private const int FallbackLimit = 100;

    private static int Offset(HmiQueryInfo query) => query.Offset ?? 0;

    private static int Limit(HmiQueryInfo query) => query.Limit ?? FallbackLimit;

    private static WorkerOperationException MissingField(string field)
        => new(WorkerFailureCategories.ValidationError, $"The '{field}' field is required.");

    private static Siemens.Engineering.HmiUnified.HmiSoftware Software(Project project, HmiQueryInfo query)
        => HmiSoftwareLocator.FindUnique(project, query.HmiName).Software;
}
