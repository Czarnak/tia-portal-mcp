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
            _ => throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError, $"Unsupported HMI worker method '{method}'."),
        };
    }

    private static Siemens.Engineering.HmiUnified.HmiSoftware Software(Project project, HmiQueryInfo query)
        => HmiSoftwareLocator.FindUnique(project, query.HmiName).Software;
}
