using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Batch;

/// <summary>
/// Read-only batch tool. Exposed in both read-only and read-write modes.
/// </summary>
[McpServerToolType]
public class ReadBatchTools
{
    [McpServerTool(Name = "execute_read_batch", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Run up to 50 non-project read operations in one call. Each item is { operationId (unique), operation, ...that operation's parameters }; projectPath is optional on every item. Reads run independently, so a failing item does not stop the others. "
    + "Valid operations (parentheses list required fields): read_hardware_config, search_equipment_catalog (query), read_cross_references, get_block_content (blockPath), list_tag_tables, get_type_content (typePath). "
    + "Large reads: bound search_equipment_catalog and read_cross_references with maxResults; oversized responses are truncated or omitted server-side with explicit markers.")]
    public static async Task<string> ExecuteReadBatch(
        OpennessWorkerClient workerClient,
        [Description("Ordered list of read operations. Each: { operationId, operation, ...operation parameters }.")] BatchOperationRequest[] operations)
    {
        var validation = BatchOperationCatalog.ValidateReadBatch(operations);
        if (!validation.IsValid)
        {
            return BatchResultFormatter.Error("execute_read_batch", validation.Error);
        }

        // Defense in depth: validate access mode before any worker invocation.
        var mode = workerClient.AccessPolicy?.Mode ?? McpAccessMode.ReadWrite;
        var accessErrors = BatchOperationCatalog.ValidateAccessMode(operations, mode);
        if (accessErrors.Count > 0)
        {
            return BatchResultFormatter.Error(
                "execute_read_batch",
                string.Join("\n", accessErrors));
        }

        var results = await BatchExecutionEngine.ExecuteReadsAsync(
            operations,
            op => BatchWorkerInvoker.InvokeAsync(workerClient, op)).ConfigureAwait(false);

        return BatchResultFormatter.ReadBatch(BatchPayloadBudget.Apply(results));
    }
}
