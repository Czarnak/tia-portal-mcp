using Siemens.Engineering;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.OpennessWorker.Worker;

internal static class WorkerExecution
{
    /// <param name="mutates">
    /// True when the request's method changes project state. Only then does an
    /// <see cref="EngineeringTargetInvocationException"/> mean "Portal rejected the mutation"
    /// (<see cref="WorkerFailureCategories.EngineeringRejected"/>); reads keep
    /// <see cref="WorkerFailureCategories.WorkerOperationFailed"/> because Openness throws the same type
    /// for an unsupported property on the current device.
    /// </param>
    internal static WorkerResponse Execute(Func<WorkerResponse> body, Func<long> portalDisposalRevision, bool mutates = false)
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
            var category = Classify(ex, mutates);
            return Failure(category, FailureMessage(ex, category), Describe(ex));
        }
        // Only HmiWriteRun.Failure sets HmiWriteOutcome: admitting it makes portal_lost win on every wrapped HMI write failure.
        catch (WorkerOperationException ex) when (ex.EngineeringError is not null || ex.HmiWriteOutcome is not null)
        {
            if (portalDisposalRevision() != before)
            {
                var response = PortalLost(ex);
                response.ConfigureNetworkDeviceOutcome = ex.ConfigureNetworkDeviceOutcome;
                response.HmiWriteOutcome = ex.HmiWriteOutcome;
                return response;
            }
            throw;
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

    internal static string Classify(EngineeringException exception, bool mutates)
        => mutates && exception is EngineeringTargetInvocationException
            ? WorkerFailureCategories.EngineeringRejected : WorkerFailureCategories.WorkerOperationFailed;

    internal static string FailureMessage(EngineeringException exception, string category)
        => category == WorkerFailureCategories.EngineeringRejected
            ? $"TIA Portal rejected the operation: {exception.Message}"
            : $"TIA Portal operation failed: {exception.Message}";

    /// <summary>
    /// The exception's type and verbatim message plus its inner chain, outermost first, bounded by
    /// <see cref="EngineeringErrorInfo.MaxInnerCount"/> and <see cref="EngineeringErrorInfo.MaxMessageChars"/>.
    /// Content importers that wrap a Siemens exception can pass this to <see cref="WorkerOperationException"/>.
    /// </summary>
    internal static EngineeringErrorInfo Describe(Exception exception)
    {
        var info = new EngineeringErrorInfo { Type = exception.GetType().Name, Message = Bound(exception.Message) };
        for (var inner = exception.InnerException; inner is not null && info.Inner.Count < EngineeringErrorInfo.MaxInnerCount; inner = inner.InnerException)
            info.Inner.Add(new EngineeringInnerErrorInfo { Type = inner.GetType().Name, Message = Bound(inner.Message) });
        return info;
    }

    private static string Bound(string? message)
        => message is null || message.Length <= EngineeringErrorInfo.MaxMessageChars
            ? message ?? string.Empty
            : message.Substring(0, EngineeringErrorInfo.MaxMessageChars);

    private static WorkerResponse PortalLost(Exception exception)
        => Failure(WorkerFailureCategories.PortalLost,
            $"The TIA Portal connection was lost: {exception.Message}. Restart TIA Portal and explicitly bind_project again. "
            + "An attempted mutation may have completed; inspect the project before retrying.");

    private static WorkerResponse Failure(string category, string error, EngineeringErrorInfo? engineeringError = null)
        => new() { Success = false, FailureCategory = category, Error = error, EngineeringError = engineeringError };
}
