using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Vci;

[McpServerToolType]
public class VciWriteTools
{
    [McpServerTool(Name = "vci_write", ReadOnly = false, Destructive = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(VciGuardedWriteResponse))]
    [Description("Execute up to 50 ordered standalone-project VCI writes: create_workspace, delete_workspace, export, sync_to_workspace, sync_to_project, delete_mapping. Omitted dryRun or false executes; true previews inventory and guards without reading status. No confirmation prompt. Software units and system blocks are skipped; directly selecting them is unsupported. Export defaults to SimaticML; unsupported formats fall back per leaf. SimaticSD export can crash TIA Portal. Import locks the TIA UI for its transaction; run compile_check afterwards and call save_project to persist project changes, workspaces and mappings. Stops at the first failed item without rollback across items or automatic replay. Client request timeouts must allow the full operation duration. Use vci_read list_mappings withStatus for status counts and inspect current state before retrying.")]
    public static Task<CallToolResult> VciWrite(OpennessWorkerClient workerClient, WriteExecution execution, VciBreadcrumbLocation breadcrumbLocation,
        [Description("Ordered VCI writes.")] VciOperationRequest[] operations,
        [Description("True previews without mutation; omitted or false executes.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => execution.RunAsync(new VciWriteDomain(workerClient, breadcrumbLocation), new WriteCall<VciOperationRequest>(
            operations ?? [], dryRun), cancellationToken: cancellationToken);
}
