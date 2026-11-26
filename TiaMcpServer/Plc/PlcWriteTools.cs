using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Plc;

[McpServerToolType]
public class PlcWriteTools
{
    [McpServerTool(Name = "plc_write", ReadOnly = false, Destructive = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(PlcGuardedWriteResponse))]
    [Description("Execute up to 50 ordered PLC software writes against one verified already-open project. Omitted dryRun or dryRun:false executes; use dryRun:true to preview effects, dependsOn and guards without mutation. No confirmation prompt is sent. The 15 operations are: update_block_logic (blockPath, content, expectedContentHash), patch_block_logic (blockPath, expectedContentHash, edits: [{oldText,newText}], optional format default xml), update_type_content (typePath, content, expectedContentHash), create_tag_table/delete_tag_table (tableName), create_tag/update_tag/delete_tag, create_user_constant/update_user_constant/delete_user_constant (tableName, name), create_block (blockPath, blockType, language, obEventClass for OB), delete_block, create_block_group, delete_block_group (blockPath). expectedContentHash is the contentHash from a plc_read of the same object in the write's format; content changed since that read is rejected with state_changed. Patch anchors match exactly once against the unchanged original base, counting overlapping matches; edit spans must not overlap. CRLF and LF compare equally. XML patches edit only the authoritative .xml document; source patches edit the complete source. The expected hash covers the whole served export, including XML bundle companions and separators. Use dryRun:true for a bounded content diff. Later items may use objects created earlier in the same call. A completed, verified block content import (full update or patch) succeeds independently of post-import compilation; inspect result.importOutcome.compileStage and compileReport. Compiler errors alone do not skip later items. A duplicate singleton OB and unreadable evidence block the call before anything runs. Creates are not checked for collisions before mutation. A native EngineeringTargetInvocationException during a mutation worker method fails that item as engineering_rejected with the Siemens message in engineeringError; rejection behavior across namespaces is not guaranteed. A same-table update_tag rename collision was observed to be silently ignored; the rename is read back and fails that item as postcondition_failed before any other property changes. Phases: preview, applied, blocked, error. Stops on first failure, with no rollback or automatic replay; re-read affected objects with plc_read before retrying. This tool never saves, downloads, or controls a PLC. Two or more source-format update_block_logic/patch_block_logic items defer compilation to one compile_check per PLC selector after the mutation loop; compilationMode and sharedCompilations describe this shared compilation. Compiler results remain informational and do not change item status, verification or root success. Cancellation leaves deferred imports uncompiled; run compile_check explicitly.")]
    public static Task<CallToolResult> PlcWrite(
        OpennessWorkerClient workerClient,
        WriteExecution execution,
        [Description("Ordered PLC write operations.")] PlcOperationRequest[] operations,
        [Description("True previews effects and guards without mutation; omitted or false executes.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => execution.RunAsync(new PlcWriteDomain(workerClient),
            new WriteCall<PlcOperationRequest>(
                operations ?? [], dryRun), cancellationToken: cancellationToken);
}
