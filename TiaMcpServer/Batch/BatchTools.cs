using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Batch;

/// <summary>
/// Batch MCP tools. A single read or write is just a one-item batch. Reads run independently;
/// writes are previewed once to obtain a batch-level safety token, then applied sequentially
/// with stop-on-first-failure (no transaction or rollback).
///
/// NOTE: This class is no longer registered as an MCP tool type. Tools have been split
/// into ReadBatchTools and WriteBatchTools. This class is kept for test backward
/// compatibility (tests reference its methods directly).
/// </summary>
public static class BatchTools
{
    private const string PreviewToolName = "preview_write_batch";
    private const string ApplyToolName = "apply_write_batch";

    [Description("Run up to 50 non-project read operations in one call. Each item is { operationId (unique), operation, ...that operation's parameters }; projectPath is optional on every item. Reads run independently, so a failing item does not stop the others. "
    + "Valid operations (parentheses list required fields): read_cross_references, get_block_content (blockPath), list_tag_tables, get_type_content (typePath). "
    + "Large reads: narrow with plcName, filter, or maxResults; oversized responses are truncated or omitted server-side with explicit markers.")]
    public static async Task<string> ExecuteReadBatch(
        OpennessWorkerClient workerClient,
        [Description("Ordered list of read operations. Each: { operationId, operation, ...operation parameters }.")] BatchOperationRequest[] operations)
    {
        var validation = BatchOperationCatalog.ValidateReadBatch(operations);
        if (!validation.IsValid)
        {
            return OperationBatchResultFormatter.Error("execute_read_batch", validation.Error);
        }

        var results = await OperationBatchExecutionEngine.ExecuteReadsAsync(
            operations,
            op => BatchWorkerInvoker.InvokeAsync(workerClient, op)).ConfigureAwait(false);

        var budgeted = OperationBatchPayloadBudget.Apply(
            results,
            toolName: "execute_read_batch",
            retryToolName: "execute_read_batch",
            narrowingHint: "Use plcName, filter, or maxResults; or split the batch.");

        return OperationBatchResultFormatter.Read("execute_read_batch", budgeted);
    }

    [Description("Preview up to 50 write operations and return one batch-level safetyToken bound to the exact ordered operation list and the combined current state. The token is single-use and expires after 10 minutes. Pass the token to apply_write_batch after reviewing the preview. All items must target the same project. "
        + "Valid operations (parentheses list required fields): update_block_logic (blockPath, yamlContent), create_block (blockPath, blockType), delete_block (blockPath), create_block_group (blockPath), delete_block_group (blockPath), create_tag_table (tableName), delete_tag_table (tableName), create_tag (tableName, name, dataType), update_tag (tableName, name), delete_tag (tableName, name), create_user_constant (tableName, name, dataType, value), update_user_constant (tableName, name), delete_user_constant (tableName, name), start_plc, stop_plc, update_type_content (typePath, sourceContent).")]
    public static async Task<string> PreviewWriteBatch(
        OpennessWorkerClient workerClient,
        WriteSafetyService safety,
        [Description("Ordered list of write operations. Each: { operationId, operation, ...operation parameters }.")] BatchOperationRequest[] operations)
    {
        var validation = BatchOperationCatalog.ValidateWriteBatch(operations);
        if (!validation.IsValid)
        {
            return OperationBatchResultFormatter.Error(PreviewToolName, validation.Error);
        }

        var projectPath = BatchSafetySnapshot.ResolveProjectPath(operations);
        var bindingGate = await workerClient.RequireVerifiedWriteBindingAsync(projectPath).ConfigureAwait(false);
        if (!bindingGate.Success)
        {
            return OperationBatchResultFormatter.Error(PreviewToolName, bindingGate.Error!);
        }

        var previewExecution = await workerClient.ExecuteWithPinnedBindingAsync(
            workerClient.BindingSnapshot,
            async () =>
            {
                var snapshot = await ReadCombinedCurrentStateAsync(workerClient, operations).ConfigureAwait(false);
                if (snapshot.Error is not null)
                {
                    return OperationBatchResultFormatter.Error(PreviewToolName, snapshot.Error);
                }

                var targets = BatchSafetySnapshot.BuildTargets(operations);
                var summary = $"Apply {operations.Length} write operation(s) sequentially; stops on first failure (no rollback). "
                    + "The current-state snapshot is read per item and is not an atomic point-in-time view.";

                return safety.CreatePreview(
                    ApplyToolName,
                    projectPath,
                    targets,
                    summary,
                    operations,
                    snapshot.CombinedState,
                    diff: null,
                    instructions: "Preview only — nothing was changed. To apply, call apply_write_batch with the identical operations list, confirm=true, and this safetyToken.");
            }).ConfigureAwait(false);
        return previewExecution.Success
            ? previewExecution.Value!
            : OperationBatchResultFormatter.Error(PreviewToolName, previewExecution.Failure!.Error!);
    }

    [Description("Apply a previewed batch of write operations sequentially, stopping on the first failure (later items are skipped; no rollback). Requires confirm=true and a safetyToken from preview_write_batch; pass the identical operations list. "
        + "Valid operations (parentheses list required fields): update_block_logic (blockPath, yamlContent), create_block (blockPath, blockType), delete_block (blockPath), create_block_group (blockPath), delete_block_group (blockPath), create_tag_table (tableName), delete_tag_table (tableName), create_tag (tableName, name, dataType), update_tag (tableName, name), delete_tag (tableName, name), create_user_constant (tableName, name, dataType, value), update_user_constant (tableName, name), delete_user_constant (tableName, name), start_plc, stop_plc, update_type_content (typePath, sourceContent).")]
    public static async Task<string> ApplyWriteBatch(
        OpennessWorkerClient workerClient,
        WriteSafetyService safety,
        [Description("Ordered list of write operations. Must match the list passed to preview_write_batch.")] BatchOperationRequest[] operations,
        [Description("Set to true to confirm the write operations. Required safety flag; operation is rejected when false.")] bool confirm = false,
        [Description("Safety token returned by preview_write_batch for this exact batch.")] string? safetyToken = null)
    {
        if (!confirm)
        {
            return OperationBatchResultFormatter.Error(
                ApplyToolName,
                "Operation not confirmed. Set confirm=true to proceed with applying the write batch.");
        }

        var validation = BatchOperationCatalog.ValidateWriteBatch(operations);
        if (!validation.IsValid)
        {
            return OperationBatchResultFormatter.Error(ApplyToolName, validation.Error);
        }

        if (string.IsNullOrWhiteSpace(safetyToken))
        {
            return OperationBatchResultFormatter.Error(
                ApplyToolName,
                $"Safety token required. Call {PreviewToolName} first, review the preview, then pass its safetyToken with confirm=true.");
        }

        var targets = BatchSafetySnapshot.BuildTargets(operations);
        var projectPath = BatchSafetySnapshot.ResolveProjectPath(operations);

        // Reject dead/mismatched tokens BEFORE the expensive per-item current-state read.
        var envelope = safety.ValidateEnvelope(
            safetyToken,
            ApplyToolName,
            projectPath,
            targets,
            operations,
            PreviewToolName);
        if (!envelope.IsValid)
        {
            return OperationBatchResultFormatter.Error(ApplyToolName, envelope.Error);
        }

        var execution = await workerClient.ExecuteWithPinnedBindingAsync(
            envelope.ProjectBinding,
            async () =>
            {
                var bindingGate = await workerClient.RequireVerifiedWriteBindingAsync(projectPath).ConfigureAwait(false);
                if (!bindingGate.Success)
                {
                    return OperationBatchResultFormatter.Error(ApplyToolName, bindingGate.Error!);
                }

                var snapshot = await ReadCombinedCurrentStateAsync(workerClient, operations).ConfigureAwait(false);
                if (snapshot.Error is not null)
                {
                    return OperationBatchResultFormatter.Error(ApplyToolName, $"Could not read current state before write. {snapshot.Error}");
                }

                var tokenValidation = safety.ValidateAndConsume(
                    safetyToken,
                    ApplyToolName,
                    projectPath,
                    targets,
                    operations,
                    snapshot.CombinedState,
                    PreviewToolName);
                if (!tokenValidation.IsValid)
                {
                    return OperationBatchResultFormatter.Error(ApplyToolName, tokenValidation.Error);
                }

                var results = await OperationBatchExecutionEngine.ApplyWritesAsync(
                    operations,
                    op => BatchWorkerInvoker.InvokeAsync(workerClient, op)).ConfigureAwait(false);

                var resultJson = OperationBatchResultFormatter.Apply(ApplyToolName, results);
                safety.AppendAudit(ApplyToolName, projectPath, targets, operations, snapshot.CombinedState, resultJson);
                return resultJson;
            }).ConfigureAwait(false);
        if (!execution.Success)
        {
            return OperationBatchResultFormatter.Error(ApplyToolName, execution.Failure!.Error!);
        }

        return execution.Value!;
    }

    private static async Task<(string CombinedState, string? Error)> ReadCombinedCurrentStateAsync(
        OpennessWorkerClient workerClient,
        BatchOperationRequest[] operations)
    {
        var states = new List<OperationBatchCurrentState>(operations.Length);
        foreach (var op in operations)
        {
            var state = await BatchWorkerInvoker.ReadCurrentStateAsync(workerClient, op).ConfigureAwait(false);
            if (!state.Success)
            {
                return (string.Empty, $"Could not read current state for operationId '{op.OperationId}' ({op.Operation}). Error: {state.Error}");
            }

            states.Add(new OperationBatchCurrentState(op.OperationId, op.Operation, state.Payload));
        }

        return (BatchSafetySnapshot.CombineCurrentState(states), null);
    }
}
