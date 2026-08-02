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
    [Description("Run up to 50 dedicated network read operations in one call. Valid operations: read_hardware_config and search_equipment_catalog (query). Reads run independently, so a failing item does not stop later operations. Each operation result is returned as declared JSON, not as a nested JSON string. Large catalog searches can be narrowed with query/maxResults or split into separate network_read calls.")]
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

        // A batch that ran is a successful MCP call even when items inside it failed: isError is
        // reserved for "the tool could not run", so the caller can tell those two cases apart.
        return StructuredToolResult.Create(
            new NetworkReadResponse(ToolName, batch.IsFullySuccessful, batch, Error: null),
            isError: false);
    }

    private static CallToolResult Error(string category, string message)
        => StructuredToolResult.Create(
            new NetworkReadResponse(ToolName, false, Batch: null, new NetworkToolError(category, message)),
            isError: true);
}
