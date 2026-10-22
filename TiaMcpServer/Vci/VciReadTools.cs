using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;

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
        OutputSchemaType = typeof(VciReadResponse))]
    [Description("Read standalone VCI workspaces with up to 50 independent operations: list_workspaces (no extra fields), list_mappings (workspaceName; optional scope, withStatus, offset and limit), supported_formats (scope naming one terminal leaf, optional workspaceName; otherwise uses the first workspace). scope is a Device-rooted sequence of {nodeType,name}; software units and system block folders are unsupported. list_mappings uses plain offset/limit paging without a snapshot; changes between pages may shift rows. withStatus costs about 0.5 seconds per returned mapping. Defaults: withStatus false, offset 0, limit 100 (1-200). Reads never open or switch projects.")]
    public static async Task<CallToolResult> VciRead(
        OpennessWorkerClient workerClient,
        [Description("Ordered VCI read operations, each with unique operationId and operation.")] VciOperationRequest[] operations,
        CancellationToken cancellationToken = default)
    {
        var validation = VciReadCatalog.ValidateRead(operations);
        if (!validation.IsValid)
        {
            return Error(validation.Category, validation.Error);
        }

        var mode = workerClient.AccessPolicy.Mode;
        var accessErrors = VciReadCatalog.ValidateAccessMode(operations, mode);
        if (accessErrors.Count != 0)
        {
            return Error(WorkerFailureCategories.AccessDenied, string.Join("\n", accessErrors));
        }

        var binding = await workerClient.RequireVerifiedBindingAsync().ConfigureAwait(false);
        if (!binding.Success)
        {
            return Error(binding.FailureCategory!, binding.Error!);
        }

        var batch = await StructuredOperationBatchExecutionEngine.ExecuteReadsAsync(
            operations,
            operation => workerClient.ReadVciAsync(TiaMcpServer.Contracts.Vci.VciMethods.ForOperation(operation.Operation), VciReadCatalog.ToQuery(operation), operation.ProjectPath),
            VciPayloadContract.Project,
            cancellationToken)
            .ConfigureAwait(false);

        // A batch that ran is a successful MCP call even when items failed: isError means "could not run".
        return StructuredToolResult.Create(Compose(ApplyBudget(batch)), isError: false);
    }

    internal static StructuredOperationBatch ApplyBudget(
        StructuredOperationBatch batch,
        int? maxItemChars = null,
        int? maxDocumentChars = null)
        => StructuredOperationBatchPayloadBudget.Apply(
            batch,
            Compose,
            ToolName,
            RetryGuidance,
            maxItemChars,
            maxDocumentChars);

    private static VciReadResponse Compose(StructuredOperationBatch batch)
        => new(ToolName, VciContractVersion.Current, batch.IsFullySuccessful,
            Error: null, Warnings: Array.Empty<string>(), Batch: batch);

    private static string RetryGuidance(StructuredOperationItem item)
        => item.Operation == "list_mappings"
            ? "Reduce limit or narrow scope, or re-run this operationId in its own vci_read call."
            : "Re-run this operationId in its own vci_read call; for one value above the limit, raise --max-item-chars and --max-document-chars.";

    internal static CallToolResult Error(string category, string message)
        => StructuredToolResult.Create(
            new VciReadResponse(ToolName, VciContractVersion.Current, false,
                new VciToolError(category, message), Warnings: Array.Empty<string>(), Batch: null),
            isError: true);
}
