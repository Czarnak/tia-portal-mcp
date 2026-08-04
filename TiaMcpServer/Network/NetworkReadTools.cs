using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

[McpServerToolType]
public class NetworkReadTools
{
    private const string ToolName = "network_read";

    [McpServerTool(
        Name = ToolName,
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(NetworkReadResponse))]
    [Description("Run up to 50 dedicated network read operations in one call. Valid operations: read_hardware_config, search_equipment_catalog (query), list_network_objects (objectKinds), and inspect_network_object (target). Reads run independently, so a failing item does not stop later operations. Each operation result is returned as declared JSON, not as a nested JSON string. Large catalog searches can be narrowed with query/maxResults or split into separate network_read calls.")]
    public static async Task<CallToolResult> NetworkRead(
        OpennessWorkerClient workerClient,
        [Description("Ordered list of dedicated network read operations. Each item is { operationId, operation, projectPath?, ...operation parameters }.")] NetworkOperationRequest[] operations)
    {
        var validation = NetworkOperationCatalog.ValidateRead(operations);
        if (!validation.IsValid)
        {
            return Error(WorkerFailureCategories.ValidationError, validation.Error);
        }

        var mode = workerClient.AccessPolicy?.Mode ?? McpAccessMode.ReadWrite;
        var accessErrors = NetworkOperationCatalog.ValidateAccessMode(operations, mode);
        if (accessErrors.Count != 0)
        {
            return Error(WorkerFailureCategories.AccessDenied, string.Join("\n", accessErrors));
        }

        var batch = await StructuredOperationBatchExecutionEngine.ExecuteReadsAsync(
            operations,
            operation => NetworkWorkerInvoker.InvokeReadAsync(workerClient, operation),
            NetworkPayloadContract.Project)
            .ConfigureAwait(false);

        var bounded = ApplyBudget(batch);

        // A batch that ran is a successful MCP call even when items inside it failed: isError is
        // reserved for "the tool could not run", so the caller can tell those two cases apart.
        return StructuredToolResult.Create(Compose(bounded), isError: false);
    }

    /// <summary>
    /// Bounds a batch against the exact <c>network_read</c> response document. Internal so tests can
    /// drive the real wiring — retry tool, per-operation guidance, envelope — at small limits.
    /// </summary>
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

    private static NetworkReadResponse Compose(StructuredOperationBatch batch)
        => new(ToolName, batch.IsFullySuccessful, batch, Error: null);

    private static string RetryGuidance(StructuredOperationItem item) => item.Operation switch
    {
        // A catalog search has its own narrowing knobs, so recommend those before a re-split.
        "search_equipment_catalog" =>
            "Narrow query or maxResults, or split the batch: re-run this operationId in its own "
                + $"{ToolName} call.",

        // A hardware read has no narrowing parameters; only a smaller batch helps.
        _ => $"Split the batch: re-run this operationId in its own {ToolName} call.",
    };

    private static CallToolResult Error(string category, string message)
        => StructuredToolResult.Create(
            new NetworkReadResponse(ToolName, false, Batch: null, new NetworkToolError(category, message)),
            isError: true);
}
