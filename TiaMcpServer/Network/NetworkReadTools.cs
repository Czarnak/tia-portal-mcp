using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

[McpServerToolType]
public class NetworkReadTools
{
    [McpServerTool(
        Name = "network_read",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Run up to 50 dedicated network read operations in one call. Valid operations: read_hardware_config and search_equipment_catalog (query). Reads run independently, so a failing item does not stop later operations. Large catalog searches can be narrowed with query/maxResults or split into separate network_read calls.")]
    public static async Task<string> NetworkRead(
        OpennessWorkerClient workerClient,
        [Description("Ordered list of dedicated network read operations. Each item is { operationId, operation, projectPath?, ...operation parameters }.")] NetworkOperationRequest[] operations)
    {
        var validation = NetworkOperationCatalog.ValidateRead(operations);
        if (!validation.IsValid)
        {
            return OperationBatchResultFormatter.Error("network_read", validation.Error);
        }

        var mode = workerClient.AccessPolicy?.Mode ?? McpAccessMode.ReadWrite;
        var accessErrors = NetworkOperationCatalog.ValidateAccessMode(operations, mode);
        if (accessErrors.Count != 0)
        {
            return OperationBatchResultFormatter.Error(
                "network_read",
                string.Join("\n", accessErrors));
        }

        var results = await OperationBatchExecutionEngine.ExecuteReadsAsync(
            operations,
            operation => NetworkWorkerInvoker.InvokeReadAsync(workerClient, operation))
            .ConfigureAwait(false);
        var budgeted = OperationBatchPayloadBudget.Apply(
            results,
            "network_read",
            "network_read",
            "Use query/maxResults or split the batch.");
        return OperationBatchResultFormatter.Read("network_read", budgeted);
    }
}
