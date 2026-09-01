using System.ComponentModel;
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
    [Description("Run up to 50 non-project read operations in one call. Each item is { operationId (unique), operation, ...that operation's parameters }; projectPath is optional on every item. Reads run independently, so a failing item does not stop the others. "
    + "Valid operations (parentheses list required fields): read_cross_references, get_block_content (blockPath), list_tag_tables, get_type_content (typePath). "
    + "Large reads: narrow with plcName, filter, or maxResults; oversized responses are truncated or omitted server-side with explicit markers.")]
    public static Task<string> ExecuteReadBatch(
        OpennessWorkerClient workerClient,
        [Description("Ordered list of read operations. Each: { operationId, operation, ...operation parameters }.")] BatchOperationRequest[] operations)
        => ReadBatchTools.ExecuteReadBatch(workerClient, operations);

    [Description("Preview up to 50 write operations and return one batch-level safetyToken bound to the exact ordered operation list and the combined current state. The token is single-use and expires after 10 minutes. Pass the token to apply_write_batch after reviewing the preview. All items must target the same project. "
        + "Valid operations (parentheses list required fields): update_block_logic (blockPath, yamlContent), create_block (blockPath, blockType), delete_block (blockPath), create_block_group (blockPath), delete_block_group (blockPath), create_tag_table (tableName), delete_tag_table (tableName), create_tag (tableName, name, dataType), update_tag (tableName, name), delete_tag (tableName, name), create_user_constant (tableName, name, dataType, value), update_user_constant (tableName, name), delete_user_constant (tableName, name), start_plc, stop_plc, update_type_content (typePath, sourceContent).")]
    public static Task<string> PreviewWriteBatch(
        OpennessWorkerClient workerClient,
        WriteSafetyService safety,
        [Description("Ordered list of write operations. Each: { operationId, operation, ...operation parameters }.")] BatchOperationRequest[] operations)
        => WriteBatchTools.PreviewWriteBatch(workerClient, safety, operations);

    [Description("Apply a previewed batch of write operations sequentially, stopping on the first failure (later items are skipped; no rollback). Requires confirm=true and a safetyToken from preview_write_batch; pass the identical operations list. "
        + "Valid operations (parentheses list required fields): update_block_logic (blockPath, yamlContent), create_block (blockPath, blockType), delete_block (blockPath), create_block_group (blockPath), delete_block_group (blockPath), create_tag_table (tableName), delete_tag_table (tableName), create_tag (tableName, name, dataType), update_tag (tableName, name), delete_tag (tableName, name), create_user_constant (tableName, name, dataType, value), update_user_constant (tableName, name), delete_user_constant (tableName, name), start_plc, stop_plc, update_type_content (typePath, sourceContent).")]
    public static Task<string> ApplyWriteBatch(
        OpennessWorkerClient workerClient,
        WriteSafetyService safety,
        [Description("Ordered list of write operations. Must match the list passed to preview_write_batch.")] BatchOperationRequest[] operations,
        [Description("Set to true to confirm the write operations. Required safety flag; operation is rejected when false.")] bool confirm = false,
        [Description("Safety token returned by preview_write_batch for this exact batch.")] string? safetyToken = null)
        => WriteBatchTools.ApplyWriteBatch(
            workerClient,
            safety,
            operations,
            confirm,
            safetyToken);
}
