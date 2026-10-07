using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Hmi;

// Not yet marked [McpServerToolType]: the tool-surface tests enumerate that attribute assembly-wide and
// still expect 6/15/15 tools. The registration task adds the attribute and the McpToolRegistration entry.
public class HmiReadTools
{
    private const string ToolName = "hmi_read";

    [McpServerTool(
        Name = ToolName,
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(HmiReadResponse))]
    [Description("Run up to 50 WinCC Unified HMI read operations in one call. Every operation except list_hmi_devices and list_project_languages takes an optional hmiName (software or device name; omitted only when the project has exactly one Unified HMI). Paged operations take offset (default 0) and limit (1-2000, default 100) and return a page { offset, limit, total, nextOffset }. Valid operations: list_hmi_devices (Unified and Classic HMI devices); list_tag_tables (optional groupPath); list_tags (optional tableName, language); get_tag (tagName; optional language); list_system_tags; list_connections;list_alarms (optional alarmKind discrete|analog, language); get_alarm (alarmName, alarmKind; optional language); list_alarm_classes (optional language); list_logs; list_logging_tags (optional dataLogName, tagName); list_screens (optional groupPath, language); list_screen_items (screenName); list_faceplate_instances (optional screenName); get_screen_navigation; get_runtime_settings; list_script_modules; list_text_and_graphic_lists; list_project_languages; validate (category tags|alarms|screens|connections|logs|alarmClasses|all; optional name, not with all). Reads run independently, so a failing item does not stop later operations. A result larger than the budget is omitted whole with guidance; lower the limit, add a narrowing field, or re-run that operationId in its own hmi_read call.")]
    public static async Task<CallToolResult> HmiRead(
        OpennessWorkerClient workerClient,
        [Description("Ordered list of HMI read operations. Each item is { operationId, operation, projectPath?, ...operation parameters }.")] HmiOperationRequest[] operations)
    {
        var validation = HmiOperationCatalog.ValidateRead(operations);
        if (!validation.IsValid)
        {
            return Error(WorkerFailureCategories.ValidationError, validation.Error);
        }

        var mode = workerClient.AccessPolicy?.Mode ?? McpAccessMode.ReadWrite;
        var accessErrors = HmiOperationCatalog.ValidateAccessMode(operations, mode);
        if (accessErrors.Count != 0)
        {
            return Error(WorkerFailureCategories.AccessDenied, string.Join("\n", accessErrors));
        }

        var batch = await StructuredOperationBatchExecutionEngine.ExecuteReadsAsync(
            operations,
            operation => HmiWorkerInvoker.InvokeReadAsync(workerClient, operation, CancellationToken.None),
            HmiPayloadContract.Project)
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

    private static HmiReadResponse Compose(StructuredOperationBatch batch)
        => new(ToolName, HmiContractVersion.Current, batch.IsFullySuccessful,
            Error: null, Warnings: Array.Empty<string>(), Batch: batch);

    private static string RetryGuidance(StructuredOperationItem item)
        => HmiOperationCatalog.IsPaged(item.Operation)
            ? $"Lower limit or add a narrowing field, or split the batch: re-run this operationId in its own {ToolName} call."
            : $"Split the batch: re-run this operationId in its own {ToolName} call.";

    private static CallToolResult Error(string category, string message)
        => StructuredToolResult.Create(
            new HmiReadResponse(ToolName, HmiContractVersion.Current, false,
                new HmiToolError(category, message), Warnings: Array.Empty<string>(), Batch: null),
            isError: true);
}
