using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Plc;

/// <summary>Dispatches validated PLC read operations to the worker client.</summary>
public static class PlcWorkerInvoker
{
    public static Task<WorkerCallResult> InvokeReadAsync(
        OpennessWorkerClient client,
        PlcOperationRequest operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return operation.Operation switch
            {
                "get_block_content" => client.GetBlockContentAsync(
                    operation.BlockPath!,
                    operation.ProjectPath,
                    PlcFormatNames.Normalize(operation.Operation, operation.Format),
                    operation.WithDependencies),
                "get_type_content" => client.GetTypeContentAsync(
                    operation.TypePath!,
                    PlcFormatNames.Normalize(operation.Operation, operation.Format),
                    operation.ProjectPath,
                    operation.WithDependencies),
                "list_tag_tables" => client.ListTagTablesAsync(
                    operation.PlcName, operation.ProjectPath, operation.TableName, operation.FolderPath),
                _ => Task.FromResult(WorkerCallResult.Fail(
                    WorkerFailureCategories.ValidationError,
                    $"Unsupported PLC read operation '{operation.Operation}'.")),
            };
        }
        catch (ArgumentException exception)
        {
            return Task.FromResult(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, exception.Message));
        }
    }
}
