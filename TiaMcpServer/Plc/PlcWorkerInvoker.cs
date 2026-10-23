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
                    projectPath: null,
                    PlcFormatNames.Normalize(operation.Operation, operation.Format),
                    operation.WithDependencies),
                "get_type_content" => client.GetTypeContentAsync(
                    operation.TypePath!,
                    PlcFormatNames.Normalize(operation.Operation, operation.Format),
                    projectPath: null,
                    operation.WithDependencies),
                "list_tag_tables" => client.ListTagTablesAsync(
                    operation.PlcName, projectPath: null, operation.TableName, operation.FolderPath),
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
        string? commonProjectPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var projectPath = commonProjectPath;
        return op.Operation switch
        {
            "update_block_logic" => InvokeUpdateBlockLogic(client, op, projectPath),
            "update_type_content" => InvokeUpdateTypeContent(client, op, projectPath),
            "create_tag_table" => client.CreateTagTableAsync(op.PlcName, op.TableName!, op.FolderPath, projectPath),
            "delete_tag_table" => client.DeleteTagTableAsync(op.PlcName, op.TableName!, op.FolderPath, projectPath),
            "create_tag" => client.CreateTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.DataType!, op.LogicalAddress, projectPath),
            "update_tag" => client.UpdateTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.NewName, op.DataType, op.LogicalAddress, op.ExternalAccessible, op.ExternalVisible, op.ExternalWritable, projectPath),
            "delete_tag" => client.DeleteTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, projectPath),
            "create_user_constant" => client.CreateUserConstantAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.DataType!, op.Value!, projectPath),
            "update_user_constant" => client.UpdateUserConstantAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.DataType, op.Value, projectPath),
            "delete_user_constant" => client.DeleteUserConstantAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, projectPath),
            "create_block" => client.CreateBlockAsync(op.BlockPath!, op.BlockType!, op.Language, op.ObEventClass, projectPath),
            "delete_block" => client.DeleteBlockAsync(op.BlockPath!, projectPath),
            "create_block_group" => client.CreateBlockGroupAsync(op.BlockPath!, projectPath),
            "delete_block_group" => client.DeleteBlockGroupAsync(op.BlockPath!, projectPath),
            _ => Task.FromResult(WorkerCallResult.Fail(
                WorkerFailureCategories.ValidationError,
                $"Unsupported PLC write operation '{op.Operation}'.")),
        };
    }

    private static Task<WorkerCallResult> InvokeUpdateBlockLogic(OpennessWorkerClient client, PlcOperationRequest op, string? projectPath)
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

        return client.UpdateBlockLogicAsync(op.BlockPath!, op.Content!, projectPath, format, op.ExpectedContentHash);
    }

    private static Task<WorkerCallResult> InvokeUpdateTypeContent(OpennessWorkerClient client, PlcOperationRequest op, string? projectPath)
        => PlcFormatNames.TryNormalize(op.Operation, op.Format, out var format, out var error)
            ? client.UpdateTypeContentAsync(op.TypePath!, op.Content!, format, projectPath, op.ExpectedContentHash)
            : Task.FromResult(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, error!));
}
