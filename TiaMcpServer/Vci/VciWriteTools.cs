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
    [Description("Execute up to 50 ordered standalone-project VCI writes: create_workspace, delete_workspace, export, sync_to_workspace, sync_to_project, delete_mapping. Omitted dryRun or false executes; true previews inventory and guards without reading status. No confirmation prompt. Software units and system blocks are skipped; directly selecting them is unsupported. Export defaults to SimaticML; unsupported formats fall back per leaf. SimaticSD export can crash TIA Portal. Import locks the TIA UI for its transaction; run compile_check after sync_to_project and call save_project to persist project changes, workspaces and mappings. Stops at the first failed item without rollback across items or automatic replay. Items are classified succeeded, failed or skipped; an item that fails after some leaves ran carries exact succeeded/failed counts and failures (up to 20, in recording order, each with category, engineeringErrorType and a message trimmed to 500 characters), with failuresOmitted counting the rest. For accumulated leaf failures, the first recorded failure decides the item category; synchronization prefilter failures can precede later native failures. Native EngineeringTargetInvocationException failures on mutation paths are engineering_rejected. Other Siemens failures, including read-only planning/create rechecks, are worker_operation_failed; portal_lost takes precedence. To recover, fix the cause and re-run the item: leaves already mapped are skipped. There is no existing-file or mapping-key collision check: Portal owns overwrite, so it may silently overwrite files, delete companion files or disturb another mapping. A later item on the same workspace (case-insensitive) is a dependent: it relies on the earlier item's plan and gets no inventory, counts or evidence guards, only its operation guards; the call warns with the dependent IDs. Verification marks completed items trusted, or superseded by a later attempted item on the same workspace; failed items have no entry; neither reads Portal back, so use vci_read list_mappings with withStatus after writes to inspect state before retrying. Client request timeouts must allow the full operation duration.")]
    public static Task<CallToolResult> VciWrite(OpennessWorkerClient workerClient, WriteExecution execution, VciBreadcrumbLocation breadcrumbLocation,
        [Description("Ordered VCI writes.")] VciOperationRequest[] operations,
        [Description("True previews without mutation; omitted or false executes.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => execution.RunAsync(new VciWriteDomain(workerClient, breadcrumbLocation), new WriteCall<VciOperationRequest>(
            operations ?? [], dryRun), cancellationToken: cancellationToken);
}
