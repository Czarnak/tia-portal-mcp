using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

public sealed class NetworkReadOperationExecutor
{
    private readonly Func<NetworkOperationRequest, Task<WorkerCallResult>> _invokeUnpaged;
    private readonly Func<NetworkOperationRequest, Task<StructuredOperationItem>> _executePaged;

    internal NetworkReadOperationExecutor(
        OpennessWorkerClient workerClient,
        HardwarePaginationCoordinator paginationCoordinator)
        : this(
            operation => NetworkWorkerInvoker.InvokeReadAsync(workerClient, operation),
            paginationCoordinator.ExecuteAsync)
    {
    }

    internal NetworkReadOperationExecutor(
        Func<NetworkOperationRequest, Task<WorkerCallResult>> invokeUnpaged,
        Func<NetworkOperationRequest, Task<StructuredOperationItem>> executePaged)
    {
        _invokeUnpaged = invokeUnpaged ?? throw new ArgumentNullException(nameof(invokeUnpaged));
        _executePaged = executePaged ?? throw new ArgumentNullException(nameof(executePaged));
    }

    public async Task<StructuredOperationItem> ExecuteAsync(NetworkOperationRequest operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (string.Equals(operation.Operation, "read_hardware_config", StringComparison.Ordinal)
            && (operation.PageSize is not null || operation.Cursor is not null))
        {
            return await _executePaged(operation).ConfigureAwait(false);
        }

        var worker = await _invokeUnpaged(operation).ConfigureAwait(false);
        return NetworkPayloadContract.Project(operation, worker);
    }
}
