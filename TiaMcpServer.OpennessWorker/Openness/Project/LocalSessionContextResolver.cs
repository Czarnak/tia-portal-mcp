using Siemens.Engineering.Multiuser;
using TiaMcpServer.Contracts.Multiuser;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Project;

internal static class LocalSessionContextResolver
{
    public static ActiveProjectContext Resolve(LocalSession owner, bool openedByWorker)
    {
        if (owner is null)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                "TIA Portal returned a null local-session owner.");

        string? path;
        try { path = owner.Project?.Path?.FullName; }
        catch (Exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                "The local-session engineering path could not be verified.");
        }
        if (path is null || !Path.IsPathRooted(path)
            || !string.Equals(Path.GetExtension(path), ".amc21", StringComparison.OrdinalIgnoreCase))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                "The local-session owner did not expose an absolute .amc21 engineering path.");

        return ActiveProjectContext.ForLocalSession(owner, openedByWorker,
            ProjectContainerKinds.LocalSession, MultiuserSessionModes.Unknown);
    }
}
