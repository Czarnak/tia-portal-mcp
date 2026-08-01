using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Batch;

/// <summary>
/// Pure helpers that build the stable, ordered snapshots a write batch binds its single
/// safety token to. Kept free of worker access so the binding logic is unit-testable.
/// </summary>
public static class BatchSafetySnapshot
{
    public static IReadOnlyList<OperationBatchTarget> BuildTargets(IReadOnlyList<BatchOperationRequest> operations)
        => operations
            .Select(op => new OperationBatchTarget(op.OperationId, op.Operation, DescribeOperation(op)))
            .ToArray();

    public static string DescribeOperation(BatchOperationRequest op) => op.Operation switch
    {
        "update_block_logic" => $"Update PLC block '{op.BlockPath}'.",
        "create_tag_table" => $"Create PLC tag table '{op.TableName}'.",
        "delete_tag_table" => $"Delete PLC tag table '{op.TableName}'.",
        "create_tag" => $"Create PLC tag '{op.Name}' in table '{op.TableName}'.",
        "update_tag" => $"Update PLC tag '{op.Name}' in table '{op.TableName}'.",
        "delete_tag" => $"Delete PLC tag '{op.Name}' from table '{op.TableName}'.",
        "create_user_constant" => $"Create PLC user constant '{op.Name}' in table '{op.TableName}'.",
        "update_user_constant" => $"Update PLC user constant '{op.Name}' in table '{op.TableName}'.",
        "delete_user_constant" => $"Delete PLC user constant '{op.Name}' from table '{op.TableName}'.",
        "update_type_content" => $"Update PLC data type '{op.TypePath}'.",
        _ => $"{op.Operation}.",
    };

    public static string CombineCurrentState(IReadOnlyList<OperationBatchCurrentState> states)
        => OperationBatchStateComposer.CombineCurrentState(states);

    public static string? ResolveProjectPath(IReadOnlyList<BatchOperationRequest> operations)
        => OperationBatchStateComposer.ResolveProjectPath(operations);
}
