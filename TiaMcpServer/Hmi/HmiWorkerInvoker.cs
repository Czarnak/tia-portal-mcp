using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Hmi;

/// <summary>Dispatches a validated hmi_read operation to the worker client.</summary>
public static class HmiWorkerInvoker
{
    public static Task<WorkerCallResult> InvokeReadAsync(
        OpennessWorkerClient client,
        HmiOperationRequest operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HmiOperationCatalog.IsOperation(operation.Operation))
        {
            return Task.FromResult(WorkerCallResult.Fail(
                WorkerFailureCategories.ValidationError,
                $"Unsupported HMI read operation '{operation.Operation}'."));
        }

        return client.ReadHmiAsync(
            HmiOperationCatalog.WorkerMethod(operation.Operation),
            HmiOperationCatalog.ToQuery(operation),
            operation.ProjectPath);
    }
}
