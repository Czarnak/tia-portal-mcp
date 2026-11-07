using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Library;

[McpServerToolType]
public class LibraryWriteTools
{
    [McpServerTool(Name = "library_write", ReadOnly = false, Destructive = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(LibraryGuardedWriteResponse))]
    [Description("Execute up to 50 ordered library writes: create_global, open_global, retrieve_global, save_global, close_global, archive_global, create_folder, delete_folder, create_master_copy, copy_master_copy, instantiate_master_copy, delete_master_copy. Every call requires a verified project binding; global operations use its Portal. Project-library and PLC operations require a standalone project. Omitted dryRun or false executes; true previews effects and guards without mutation or confirmation. Read-write prompts once when the call contains any of the six global-library lifecycle operations (create/open/retrieve/save/close/archive); full runs without a prompt. Content-only calls never prompt. No intra-call dependencies: items cannot use objects created earlier in this call. Stops at the first failure without rollback or automatic replay. Verification trusts completed native calls or marks them superseded; nothing is read back. Inspect current state before retrying.")]
    public static Task<CallToolResult> LibraryWrite(OpennessWorkerClient workerClient, WriteExecution execution,
        [Description("Ordered library writes, with unique operationId values.")] LibraryWriteItem[] operations,
        [Description("True previews without mutation; omitted or false executes.")] bool dryRun = false,
        McpServer? server = null, CancellationToken cancellationToken = default)
        => execution.RunAsync(new LibraryWriteDomain(workerClient), new WriteCall<LibraryWriteItem>(operations ?? [], dryRun),
            new WriteConfirmationContext(server is null ? null : UserConfirmation.For(server, TimeSpan.FromMinutes(2))),
            cancellationToken: cancellationToken);
}
