using Siemens.Engineering;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;
/// <summary>Routes an <c>hmi_*</c> worker method to its reader. Unknown methods are denied; reads never bind, switch or open a project.</summary>
public static class HmiReadDispatch
{
    public static object Read(ProjectBase project, string method, HmiQueryInfo query)
    {
        if (query.Language is not null)
        {
            HmiTextMapper.RequireProjectLanguage(project, query.Language);
        }

        // A continuation pages the held snapshot of its own software, so it skips the device walk.
        var snapshot = query.SnapshotId is null ? null : HmiSnapshotStore.Shared.Take(project, query.SnapshotId, method);
        var paging = new HmiPaging(query.Offset ?? 0, query.Limit ?? FallbackLimit, method, snapshot);
        Siemens.Engineering.HmiUnified.HmiSoftware Software()
            => snapshot?.Software ?? HmiSoftwareLocator.FindUnique(project, query.HmiName).Software;

        return method switch
        {
            "hmi_list_hmi_devices" => HmiSoftwareInfoReader.ListDevices(project),
            "hmi_get_runtime_settings" => HmiSoftwareInfoReader.ReadRuntimeSettings(Software()),
            "hmi_list_script_modules" => HmiSoftwareInfoReader.ListScriptModules(Software()),
            "hmi_list_text_and_graphic_lists" => HmiSoftwareInfoReader.ListTextAndGraphicLists(Software()),
            "hmi_list_project_languages" => HmiSoftwareInfoReader.ListProjectLanguages(project),
            "hmi_list_tag_tables" => HmiTagReader.ListTagTables(Software(), query.GroupPath),
            "hmi_list_tags" => HmiTagReader.ListTags(Software(), query.TableName, query.Language, paging, project),
            "hmi_get_tag" => HmiTagReader.GetTag(Software(), query.TagName ?? throw MissingField("tagName"), query.Language),
            "hmi_list_system_tags" => HmiTagReader.ListSystemTags(Software(), paging, project),
            "hmi_list_logging_tags" => HmiTagReader.ListLoggingTags(
                Software(), query.DataLogName, query.TagName, paging, project),
            "hmi_list_connections" => HmiConnectionReader.ListConnections(Software()),
            "hmi_list_alarms" => HmiAlarmReader.ListAlarms(Software(), query.AlarmKind, query.Language, paging, project),
            "hmi_get_alarm" => HmiAlarmReader.GetAlarm(
                Software(),
                query.AlarmName ?? throw MissingField("alarmName"),
                query.AlarmKind ?? throw MissingField("alarmKind"),
                query.Language),
            "hmi_list_alarm_classes" => HmiAlarmReader.ListAlarmClasses(Software()),
            "hmi_list_logs" => HmiLogReader.ListLogs(Software()),
            "hmi_list_screens" => HmiScreenReader.ListScreens(Software(), query.GroupPath, query.Language),
            "hmi_list_screen_items" => HmiScreenReader.ListScreenItems(
                Software(), query.ScreenName ?? throw MissingField("screenName"), paging, project),
            "hmi_list_faceplate_instances" => HmiScreenReader.ListFaceplateInstances(
                Software(), query.ScreenName, paging, project),
            "hmi_get_screen_navigation" => HmiScreenReader.GetScreenNavigation(Software()),
            "hmi_validate" => HmiValidationReader.Validate(
                Software(), query.Category ?? throw MissingField("category"), query.Name, paging, project),
            _ => throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError, $"Unsupported HMI worker method '{method}'."),
        };
    }

    // The host always sends concrete paging values for paged operations (HmiOperationCatalog.DefaultLimit); these only guard a malformed request.
    private const int FallbackLimit = 100;

    private static WorkerOperationException MissingField(string field)
        => new(WorkerFailureCategories.ValidationError, $"The '{field}' field is required.");
}
