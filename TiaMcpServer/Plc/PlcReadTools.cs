using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
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
        OutputSchemaType = typeof(PlcReadResponse))]
    [Description("Run up to 50 PLC read operations in one call. Valid operations: get_block_content (blockPath, optional format xml|source and withDependencies), get_type_content (typePath, optional format and withDependencies), and list_tag_tables (optional plcName; omitted reads every PLC). Reads run independently, so a failing item does not stop later operations. Content results are { format, content, contentHash, warnings }; contentHash is null for withDependencies reads. list_tag_tables returns { isComplete, messages, plcs[] } as declared JSON. A result larger than the budget is omitted whole with guidance; narrow it or re-run that operationId in its own plc_read call.")]
    public static async Task<CallToolResult> PlcRead(
        OpennessWorkerClient workerClient,
        [Description("Ordered list of PLC read operations. Each item is { operationId, operation, projectPath?, ...operation parameters }.")] PlcOperationRequest[] operations)
    {
        var validation = PlcOperationCatalog.ValidateRead(operations);
        if (!validation.IsValid)
        {
            return Error(WorkerFailureCategories.ValidationError, validation.Error);
        }

        var mode = workerClient.AccessPolicy?.Mode ?? McpAccessMode.ReadWrite;
        var accessErrors = PlcOperationCatalog.ValidateAccessMode(operations, mode);
        if (accessErrors.Count != 0)
        {
            return Error(WorkerFailureCategories.AccessDenied, string.Join("\n", accessErrors));
        }

        var batch = await StructuredOperationBatchExecutionEngine.ExecuteReadsAsync(
            operations,
            operation => PlcWorkerInvoker.InvokeReadAsync(workerClient, operation, CancellationToken.None),
            PlcPayloadContract.Project)
            .ConfigureAwait(false);

        // A batch that ran is a successful MCP call even when items failed: isError means "could not run".
        return StructuredToolResult.Create(Compose(ApplyBudget(batch)), isError: false);
    }

    internal static StructuredOperationBatch ApplyBudget(
        StructuredOperationBatch batch,
        int maxItemChars = StructuredOperationBatchPayloadBudget.MaxItemChars,
        int maxDocumentChars = StructuredOperationBatchPayloadBudget.MaxDocumentChars)
        => StructuredOperationBatchPayloadBudget.Apply(
            batch,
            Compose,
            ToolName,
            RetryGuidance,
            maxItemChars,
            maxDocumentChars);

    private static PlcReadResponse Compose(StructuredOperationBatch batch)
        => new(ToolName, PlcContractVersion.Current, batch.IsFullySuccessful,
            Error: null, Warnings: Array.Empty<string>(), Batch: batch);

    private static string RetryGuidance(StructuredOperationItem item) => item.Operation switch
    {
        "get_block_content" or "get_type_content" =>
            "Read a smaller object or drop withDependencies, or split the batch: re-run this operationId in its own "
                + $"{ToolName} call.",
        "list_tag_tables" =>
            $"Narrow with plcName, or split the batch: re-run this operationId in its own {ToolName} call.",
        _ => $"Split the batch: re-run this operationId in its own {ToolName} call.",
    };

    private static CallToolResult Error(string category, string message)
        => StructuredToolResult.Create(
            new PlcReadResponse(ToolName, PlcContractVersion.Current, false,
                new PlcToolError(category, message), Warnings: Array.Empty<string>(), Batch: null),
            isError: true);
}
