using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>Dispatches validated PLC read and write operations to the worker client.</summary>
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
                    format: PlcFormatNames.Normalize(operation.Operation, operation.Format),
                    withDependencies: operation.WithDependencies),
                "get_type_content" => client.GetTypeContentAsync(
                    operation.TypePath!,
                    format: PlcFormatNames.Normalize(operation.Operation, operation.Format),
                    withDependencies: operation.WithDependencies),
                "list_tag_tables" => client.ListTagTablesAsync(
                    operation.PlcName, tableName: operation.TableName, folderPath: operation.FolderPath),
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

    /// <summary>Executes one validated write operation against the worker.</summary>
    public static Task<WorkerCallResult> InvokeWriteAsync(
        OpennessWorkerClient client,
        PlcOperationRequest op,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return op.Operation switch
        {
            "update_block_logic" => InvokeUpdateBlockLogic(client, op),
            "update_type_content" => InvokeUpdateTypeContent(client, op),
            "create_tag_table" => client.CreateTagTableAsync(op.PlcName, op.TableName!, op.FolderPath),
            "delete_tag_table" => client.DeleteTagTableAsync(op.PlcName, op.TableName!, op.FolderPath),
            "create_tag" => client.CreateTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.DataType!, op.LogicalAddress),
            "update_tag" => client.UpdateTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.NewName, op.DataType, op.LogicalAddress, op.ExternalAccessible, op.ExternalVisible, op.ExternalWritable),
            "delete_tag" => client.DeleteTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!),
            "create_user_constant" => client.CreateUserConstantAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.DataType!, op.Value!),
            "update_user_constant" => client.UpdateUserConstantAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.DataType, op.Value),
            "delete_user_constant" => client.DeleteUserConstantAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!),
            "create_block" => client.CreateBlockAsync(op.BlockPath!, op.BlockType!, op.Language, op.ObEventClass),
            "delete_block" => client.DeleteBlockAsync(op.BlockPath!),
            "create_block_group" => client.CreateBlockGroupAsync(op.BlockPath!),
            "delete_block_group" => client.DeleteBlockGroupAsync(op.BlockPath!),
            _ => Task.FromResult(WorkerCallResult.Fail(
                WorkerFailureCategories.ValidationError,
                $"Unsupported PLC write operation '{op.Operation}'.")),
        };
    }

    private static Task<WorkerCallResult> InvokeUpdateBlockLogic(OpennessWorkerClient client, PlcOperationRequest op)
    {
        if (!PlcFormatNames.TryNormalize(op.Operation, op.Format, out var format, out var error))
        {
            // Rejected before any worker call: the import never started, and the caller gets typed evidence of that.
            return Task.FromResult(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, error!) with
            {
                DispatchState = WorkerDispatchState.NotSent,
                BlockImportOutcome = BlockImportOutcomeSynthesizer.Synthesize(
                    WorkerDispatchState.NotSent,
                    normalizedFormatOrNull: null),
            });
        }

        return client.UpdateBlockLogicAsync(op.BlockPath!, op.Content!, format: format, expectedContentHash: op.ExpectedContentHash);
    }

    private static Task<WorkerCallResult> InvokeUpdateTypeContent(OpennessWorkerClient client, PlcOperationRequest op)
        => PlcFormatNames.TryNormalize(op.Operation, op.Format, out var format, out var error)
            ? client.UpdateTypeContentAsync(op.TypePath!, op.Content!, format: format, expectedContentHash: op.ExpectedContentHash)
            : Task.FromResult(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, error!));
}
