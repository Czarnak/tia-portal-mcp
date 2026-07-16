using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Batch;

/// <summary>
/// Batch MCP tools. A single read or write is just a one-item batch. Reads run independently;
/// writes are previewed once to obtain a batch-level safety token, then applied sequentially
/// with stop-on-first-failure (no transaction or rollback).
/// </summary>
[McpServerToolType]
public static class BatchTools
{
    private const string PreviewToolName = "preview_write_batch";
    private const string ApplyToolName = "apply_write_batch";

    [McpServerTool(Name = "execute_read_batch")]
    [Description("Run up to 50 read operations in one call. Each item is { operationId (unique), operation, ...that operation's parameters }; projectPath is optional on every item. Reads run independently, so a failing item does not stop the others. "
        + "Valid operations (parentheses list required fields): browse_project_tree, read_hardware_config, read_cross_references, search_equipment_catalog (query), get_block_content (blockPath), list_tag_tables, compile_check, get_project_status. "
        + "Large projects: bound payloads with depth/startPath (browse_project_tree) and maxResults (search_equipment_catalog, read_cross_references); oversized responses are truncated server-side with an explicit marker.")]
    public static async Task<string> ExecuteReadBatch(
        OpennessWorkerClient workerClient,
        [Description("Ordered list of read operations. Each: { operationId, operation, ...operation parameters }.")] BatchOperationRequest[] operations)
    {
        var validation = BatchOperationCatalog.ValidateReadBatch(operations);
        if (!validation.IsValid)
        {
            return BatchResultFormatter.Error("execute_read_batch", validation.Error);
        }

        var results = await BatchExecutionEngine.ExecuteReadsAsync(
            operations,
            op => BatchWorkerInvoker.InvokeAsync(workerClient, op)).ConfigureAwait(false);

        return BatchResultFormatter.ReadBatch(BatchPayloadBudget.Apply(results));
    }

    [McpServerTool(Name = "preview_write_batch")]
    [Description("Preview up to 50 write operations and return one batch-level safetyToken bound to the exact ordered operation list and the combined current state. The token is single-use and expires after 10 minutes. Pass the token to apply_write_batch after reviewing the preview. All items must target the same project. "
        + "Valid operations (parentheses list required fields): update_block_logic (blockPath, yamlContent), create_block (blockPath, blockType), delete_block (blockPath), create_block_group (blockPath), delete_block_group (blockPath), create_tag_table (tableName), delete_tag_table (tableName), create_tag (tableName, name, dataType), update_tag (tableName, name), delete_tag (tableName, name), create_user_constant (tableName, name, dataType, value), update_user_constant (tableName, name), delete_user_constant (tableName, name), add_network_device (typeIdentifier, deviceName), configure_network_device (deviceName), start_plc, stop_plc.")]
    public static async Task<string> PreviewWriteBatch(
        OpennessWorkerClient workerClient,
        [Description("Ordered list of write operations. Each: { operationId, operation, ...operation parameters }.")] BatchOperationRequest[] operations)
    {
        var validation = BatchOperationCatalog.ValidateWriteBatch(operations);
        if (!validation.IsValid)
        {
            return BatchResultFormatter.Error(PreviewToolName, validation.Error);
        }

        var snapshot = await ReadCombinedCurrentStateAsync(workerClient, operations).ConfigureAwait(false);
        if (snapshot.Error is not null)
        {
            return BatchResultFormatter.Error(PreviewToolName, snapshot.Error);
        }

        var targets = BatchSafetySnapshot.BuildTargets(operations);
        var projectPath = BatchSafetySnapshot.ResolveProjectPath(operations);
        var summary = $"Apply {operations.Length} write operation(s) sequentially; stops on first failure (no rollback). "
            + "The current-state snapshot is read per item and is not an atomic point-in-time view.";

        return WriteSafetyService.Shared.CreatePreview(
            ApplyToolName,
            projectPath,
            targets,
            summary,
            operations,
            snapshot.CombinedState);
    }

    [McpServerTool(Name = "apply_write_batch")]
    [Description("Apply a previewed batch of write operations sequentially, stopping on the first failure (later items are skipped; no rollback). Requires confirm=true and a safetyToken from preview_write_batch; pass the identical operations list. "
        + "Valid operations (parentheses list required fields): update_block_logic (blockPath, yamlContent), create_block (blockPath, blockType), delete_block (blockPath), create_block_group (blockPath), delete_block_group (blockPath), create_tag_table (tableName), delete_tag_table (tableName), create_tag (tableName, name, dataType), update_tag (tableName, name), delete_tag (tableName, name), create_user_constant (tableName, name, dataType, value), update_user_constant (tableName, name), delete_user_constant (tableName, name), add_network_device (typeIdentifier, deviceName), configure_network_device (deviceName), start_plc, stop_plc.")]
    public static async Task<string> ApplyWriteBatch(
        OpennessWorkerClient workerClient,
        [Description("Ordered list of write operations. Must match the list passed to preview_write_batch.")] BatchOperationRequest[] operations,
        [Description("Set to true to confirm the write operations. Required safety flag; operation is rejected when false.")] bool confirm = false,
        [Description("Safety token returned by preview_write_batch for this exact batch.")] string? safetyToken = null)
    {
        if (!confirm)
        {
            return BatchResultFormatter.Error(
                ApplyToolName,
                "Operation not confirmed. Set confirm=true to proceed with applying the write batch.");
        }

        var validation = BatchOperationCatalog.ValidateWriteBatch(operations);
        if (!validation.IsValid)
        {
            return BatchResultFormatter.Error(ApplyToolName, validation.Error);
        }

        if (string.IsNullOrWhiteSpace(safetyToken))
        {
            return BatchResultFormatter.Error(
                ApplyToolName,
                $"Safety token required. Call {PreviewToolName} first, review the preview, then pass its safetyToken with confirm=true.");
        }

        var targets = BatchSafetySnapshot.BuildTargets(operations);
        var projectPath = BatchSafetySnapshot.ResolveProjectPath(operations);

        // Reject dead/mismatched tokens BEFORE the expensive per-item current-state read.
        var envelope = WriteSafetyService.Shared.ValidateEnvelope(
            safetyToken,
            ApplyToolName,
            projectPath,
            targets,
            operations,
            PreviewToolName);
        if (!envelope.IsValid)
        {
            return BatchResultFormatter.Error(ApplyToolName, envelope.Error);
        }

        var snapshot = await ReadCombinedCurrentStateAsync(workerClient, operations).ConfigureAwait(false);
        if (snapshot.Error is not null)
        {
            return BatchResultFormatter.Error(ApplyToolName, $"Could not read current state before write. {snapshot.Error}");
        }

        var tokenValidation = WriteSafetyService.Shared.ValidateAndConsume(
            safetyToken,
            ApplyToolName,
            projectPath,
            targets,
            operations,
            snapshot.CombinedState,
            PreviewToolName);
        if (!tokenValidation.IsValid)
        {
            return BatchResultFormatter.Error(ApplyToolName, tokenValidation.Error);
        }

        var results = await BatchExecutionEngine.ApplyWritesAsync(
            operations,
            op => BatchWorkerInvoker.InvokeAsync(workerClient, op)).ConfigureAwait(false);

        var resultJson = BatchResultFormatter.ApplyBatch(results);
        WriteSafetyService.Shared.AppendAudit(ApplyToolName, projectPath, targets, operations, snapshot.CombinedState, resultJson);
        return resultJson;
    }

    private static async Task<(string CombinedState, string? Error)> ReadCombinedCurrentStateAsync(
        OpennessWorkerClient workerClient,
        BatchOperationRequest[] operations)
    {
        var states = new List<BatchCurrentState>(operations.Length);
        foreach (var op in operations)
        {
            var state = await BatchWorkerInvoker.ReadCurrentStateAsync(workerClient, op).ConfigureAwait(false);
            if (!state.Success)
            {
                return (string.Empty, $"Could not read current state for operationId '{op.OperationId}' ({op.Operation}). Error: {state.Error}");
            }

            states.Add(new BatchCurrentState(op.OperationId, op.Operation, state.Payload));
        }

        return (BatchSafetySnapshot.CombineCurrentState(states), null);
    }
}
