using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Plc;

[McpServerToolType]
public class PlcReadTools
{
    private const string ToolName = "plc_read";

    [McpServerTool(
        Name = ToolName,
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(OperationBatchReadResponse))]
    [Description("Run up to 50 PLC read operations in one call. Valid operations: get_block_content (blockPath, optional format xml|source and withDependencies), get_type_content (typePath, optional format and withDependencies), and list_tag_tables (optional plcName, omitted reads every PLC; optional tableName and folderPath narrow to one table or one tag-table folder). Reads run independently, so a failing item does not stop later operations. Content results are { format, content, contentHash, warnings }; contentHash is null for withDependencies reads. list_tag_tables returns { isComplete, messages, plcs[] } as declared JSON. A result larger than the budget is omitted whole with guidance; narrow it or re-run that operationId in its own plc_read call.")]
    public static Task<CallToolResult> PlcRead(
        OpennessWorkerClient workerClient,
        [Description("Ordered list of PLC read operations. Each item is { operationId, operation, ...operation parameters }.")] PlcOperationRequest[] operations,
        CancellationToken cancellationToken = default)
        => BatchReadTool.RunAsync(workerClient, operations, ToolName, PlcContractVersion.Current,
            PlcOperationCatalog.ValidateRead, PlcOperationCatalog.ValidateAccessMode,
            async operation => PlcPayloadContract.Project(operation,
                await PlcWorkerInvoker.InvokeReadAsync(workerClient, operation, cancellationToken).ConfigureAwait(false)),
            RetryGuidance, cancellationToken);

    internal static StructuredOperationBatch ApplyBudget(
        StructuredOperationBatch batch,
        int? maxItemChars = null,
        int? maxDocumentChars = null)
        => BatchReadTool.ApplyBudget(batch, ToolName, PlcContractVersion.Current,
            RetryGuidance, maxItemChars, maxDocumentChars);

    private static string RetryGuidance(StructuredOperationItem item) => item.Operation switch
    {
        "get_block_content" or "get_type_content" =>
            "Read a smaller object or drop withDependencies, or split the batch: re-run this operationId in its own "
                + $"{ToolName} call. A single object above the limit needs the server restarted with "
                + "--max-item-chars (or TIA_MCP_MAX_ITEM_CHARS) at or above originalChars and --max-document-chars "
                + "(or TIA_MCP_MAX_DOCUMENT_CHARS) with headroom above it for the response envelope.",
        "list_tag_tables" =>
            "Narrow with plcName, folderPath or tableName (one table is the narrowest), or split the batch: "
                + $"re-run this operationId in its own {ToolName} call.",
        _ => $"Split the batch: re-run this operationId in its own {ToolName} call.",
    };

    internal static CallToolResult Error(string category, string message)
        => BatchReadTool.Error(ToolName, PlcContractVersion.Current, category, message);
}
