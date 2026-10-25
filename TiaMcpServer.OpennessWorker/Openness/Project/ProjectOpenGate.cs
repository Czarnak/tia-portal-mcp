using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Project;

internal static class ProjectOpenGate
{
    internal static void Require(string? currentPath, string? requestedPath, McpAccessMode mode,
        bool allowRequestedNotOpen, bool allowNoProject)
    {
        switch (ProjectOpenPolicy.Decide(currentPath, requestedPath))
        {
            case ProjectOpenDecision.RequestedNotOpen:
                if (allowRequestedNotOpen)
                    return;
                throw new WorkerOperationException(WorkerFailureCategories.AccessDenied,
                    ProjectOpenPolicy.NotOpenMessage(requestedPath!, mode));
            case ProjectOpenDecision.Refuse:
                throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                    ProjectOpenPolicy.RefusalMessage(currentPath!, requestedPath!, mode));
            default:
                if (currentPath is null && !allowNoProject)
                    throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                        ProjectOpenPolicy.NoProjectOpenMessage(mode));
                return;
        }
    }
}
