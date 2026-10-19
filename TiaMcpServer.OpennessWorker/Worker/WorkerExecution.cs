using Siemens.Engineering;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.OpennessWorker.Worker;

internal static class WorkerExecution
{
    internal static WorkerResponse Execute(Func<WorkerResponse> body, Func<long> portalDisposalRevision)
    {
        var before = portalDisposalRevision();
        try
        {
            return body();
        }
        catch (EngineeringException ex)
        {
            if (portalDisposalRevision() != before)
                return PortalLost(ex);
            return Failure(WorkerFailureCategories.WorkerOperationFailed, $"TIA Portal operation failed: {ex.Message}");
        }
        catch (NonRecoverableException ex)
        {
            return PortalLost(ex);
        }
        catch (InvalidOperationException ex)
        {
            return Failure(WorkerFailureCategories.WorkerOperationFailed, ex.Message);
        }
        catch (System.IO.IOException ex)
        {
            return Failure(WorkerFailureCategories.WorkerOperationFailed, ex.Message);
        }
    }

    private static WorkerResponse PortalLost(Exception exception)
        => Failure(WorkerFailureCategories.PortalLost,
            $"The TIA Portal connection was lost: {exception.Message}. Restart TIA Portal and explicitly bind_project again. "
            + "An attempted mutation may have completed; inspect the project before retrying.");

    private static WorkerResponse Failure(string category, string error)
        => new() { Success = false, FailureCategory = category, Error = error };
}
