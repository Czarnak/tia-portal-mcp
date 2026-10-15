using Siemens.Engineering;
using Siemens.Engineering.VersionControl;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Vci;

using Project = Siemens.Engineering.Project;

public static class VciDispatch
{
    public static object Read(ProjectBase root, string method, VciQueryInfo query)
    {
        try
        {
            if (!VciMethods.Read.Contains(method))
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                    $"Unsupported VCI worker method '{method}'.");
            var project = root as Project ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                "VCI reads require a standalone project.");
            var service = project.GetService<VersionControlInterface>()
                ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                    "The project does not expose the Version Control Interface.");
            return method switch
            {
                "vci_list_workspaces" => VciReader.ListWorkspaces(service),
                "vci_list_mappings" => VciReader.ListMappings(service, project, query),
                "vci_supported_formats" => VciReader.SupportedFormats(service, project, query),
                _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                    $"Unsupported VCI worker method '{method}'.")
            };
        }
        catch (NonRecoverableException ex)
        {
            throw new WorkerOperationException(WorkerFailureCategories.PortalLost,
                $"TIA Portal was lost during the VCI read: {ex.Message}");
        }
    }
}
