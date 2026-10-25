using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Vci;

[McpServerToolType]
public class VciReadTools
{
    private const string ToolName = "vci_read";

    [McpServerTool(
        Name = ToolName,
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(OperationBatchReadResponse))]
    [Description("Read standalone VCI workspaces with up to 50 independent operations: list_workspaces (no extra fields), list_mappings (workspaceName; optional scope, withStatus, offset and limit), supported_formats (scope naming one terminal leaf, optional workspaceName; otherwise uses the first workspace). scope is a Device-rooted sequence of {nodeType,name}; software units and system block folders are unsupported. list_mappings uses plain offset/limit paging without a snapshot; changes between pages may shift rows. withStatus costs about 0.5 seconds per returned mapping. Defaults: withStatus false, offset 0, limit 100 (1-200). Reads never open or switch projects.")]
    public static Task<CallToolResult> VciRead(
        OpennessWorkerClient workerClient,
        [Description("Ordered VCI read operations, each with unique operationId and operation.")] VciOperationRequest[] operations,
        CancellationToken cancellationToken = default)
        => BatchReadTool.RunAsync(workerClient, operations, ToolName, VciContractVersion.Current,
            VciReadCatalog.ValidateRead, VciReadCatalog.ValidateAccessMode,
            async operation => VciPayloadContract.Project(operation,
                await workerClient.ReadVciAsync(TiaMcpServer.Contracts.Vci.VciMethods.ForOperation(operation.Operation),
                    VciReadCatalog.ToQuery(operation)).ConfigureAwait(false)),
            RetryGuidance, cancellationToken);

    internal static StructuredOperationBatch ApplyBudget(
        StructuredOperationBatch batch,
        int? maxItemChars = null,
        int? maxDocumentChars = null)
        => BatchReadTool.ApplyBudget(batch, ToolName, VciContractVersion.Current,
            RetryGuidance, maxItemChars, maxDocumentChars);

    private static string RetryGuidance(StructuredOperationItem item)
        => item.Operation == "list_mappings"
            ? "Reduce limit or narrow scope, or re-run this operationId in its own vci_read call."
            : "Re-run this operationId in its own vci_read call; for one value above the limit, raise --max-item-chars and --max-document-chars.";

    internal static CallToolResult Error(string category, string message)
        => BatchReadTool.Error(ToolName, VciContractVersion.Current, category, message);
}
