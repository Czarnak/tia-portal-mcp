using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

public sealed class NetworkReadOperationExecutor
{
    private readonly Func<NetworkOperationRequest, Task<WorkerCallResult>> _invokeUnpaged;
    private readonly Func<NetworkOperationRequest, Task<StructuredOperationItem>> _executePaged;
    private readonly Func<NetworkOperationRequest, Task<StructuredOperationItem>> _executeObjectPage;

    internal NetworkReadOperationExecutor(
        OpennessWorkerClient workerClient,
        HardwarePaginationCoordinator paginationCoordinator,
        NetworkObjectPaginationCoordinator objectPaginationCoordinator)
        : this(
            operation => NetworkWorkerInvoker.InvokeReadAsync(workerClient, operation),
            paginationCoordinator.ExecuteAsync,
            objectPaginationCoordinator.ExecuteAsync)
    {
    }

    internal NetworkReadOperationExecutor(
        Func<NetworkOperationRequest, Task<WorkerCallResult>> invokeUnpaged,
        Func<NetworkOperationRequest, Task<StructuredOperationItem>> executePaged,
        Func<NetworkOperationRequest, Task<StructuredOperationItem>> executeObjectPage)
    {
        _invokeUnpaged = invokeUnpaged ?? throw new ArgumentNullException(nameof(invokeUnpaged));
        _executePaged = executePaged ?? throw new ArgumentNullException(nameof(executePaged));
        _executeObjectPage = executeObjectPage ?? throw new ArgumentNullException(nameof(executeObjectPage));
    }

    public async Task<StructuredOperationItem> ExecuteAsync(NetworkOperationRequest operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (string.Equals(operation.Operation, "list_network_objects", StringComparison.Ordinal))
        {
            return await _executeObjectPage(operation).ConfigureAwait(false);
        }
        if (string.Equals(operation.Operation, "read_hardware_config", StringComparison.Ordinal)
            && (operation.PageSize is not null || operation.Cursor is not null))
        {
            return await _executePaged(operation).ConfigureAwait(false);
        }

        var worker = await _invokeUnpaged(operation).ConfigureAwait(false);
        return NetworkPayloadContract.Project(operation, worker);
    }
}
