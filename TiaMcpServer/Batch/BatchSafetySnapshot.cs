using System.Text;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.Batch;

/// <summary>Human-readable, exactly-bound description of one write item in a batch.</summary>
public sealed record BatchOperationTarget(string OperationId, string Operation, string Summary);

/// <summary>Per-item current-state reading captured during preview/apply.</summary>
public sealed record BatchCurrentState(string OperationId, string Operation, string CurrentState);

/// <summary>
/// Pure helpers that build the stable, ordered snapshots a write batch binds its single
/// safety token to. Kept free of worker access so the binding logic is unit-testable.
/// </summary>
public static class BatchSafetySnapshot
{
    private const string StateSeparator = "\n--- batch item ---\n";

    public static IReadOnlyList<BatchOperationTarget> BuildTargets(IReadOnlyList<BatchOperationRequest> operations)
        => operations
            .Select(op => new BatchOperationTarget(op.OperationId, op.Operation, DescribeOperation(op)))
            .ToArray();

    public static string DescribeOperation(BatchOperationRequest op) => op.Operation switch
    {
        // The format decides which pipeline runs and therefore what a failed write can leave
        // behind, so the preview names it. Omitted format keeps the original wording, because
        // callers' previews must not change when they did not opt in.
        "update_block_logic" => string.Equals(op.Format, SourceFormatNames.Source, StringComparison.OrdinalIgnoreCase)
            ? $"Update PLC block '{op.BlockPath}' from source format."
            : $"Update PLC block '{op.BlockPath}'.",
        "create_tag_table" => $"Create PLC tag table '{op.TableName}'.",
        "delete_tag_table" => $"Delete PLC tag table '{op.TableName}'.",
        "create_tag" => $"Create PLC tag '{op.Name}' in table '{op.TableName}'.",
        "update_tag" => $"Update PLC tag '{op.Name}' in table '{op.TableName}'.",
        "delete_tag" => $"Delete PLC tag '{op.Name}' from table '{op.TableName}'.",
        "create_user_constant" => $"Create PLC user constant '{op.Name}' in table '{op.TableName}'.",
        "update_user_constant" => $"Update PLC user constant '{op.Name}' in table '{op.TableName}'.",
        "delete_user_constant" => $"Delete PLC user constant '{op.Name}' from table '{op.TableName}'.",
        "add_network_device" => $"Add network device '{op.DeviceName}' using catalog type '{op.TypeIdentifier}'.",
        "configure_network_device" => $"Configure network properties for device '{op.DeviceName}'.",
        "update_type_content" => $"Update PLC data type '{op.TypePath}'.",
        _ => $"{op.Operation}.",
    };

    public static string CombineCurrentState(IReadOnlyList<BatchCurrentState> states)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < states.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(StateSeparator);
            }

            var state = states[i];
            builder.Append(state.OperationId).Append("::").Append(state.Operation).Append('\n').Append(state.CurrentState);
        }

        return builder.ToString();
    }

    public static string? ResolveProjectPath(IReadOnlyList<BatchOperationRequest> operations)
        => operations.FirstOrDefault(op => !string.IsNullOrWhiteSpace(op.ProjectPath))?.ProjectPath;
}
