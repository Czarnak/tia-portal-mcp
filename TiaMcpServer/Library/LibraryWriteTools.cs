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
    [Description("Execute up to 50 ordered library writes: create_global, open_global, retrieve_global, save_global, close_global, archive_global, create_folder, delete_folder, create_master_copy, copy_master_copy, instantiate_master_copy, delete_master_copy, create_type, create_type_version, release_type_version, set_default_type_version, synchronize_type, instantiate_type_version, delete_type, delete_type_version, set_comments, set_languages. Every call requires a verified project binding; global operations use its Portal. Project-library and PLC operations require a standalone project. Omitted dryRun or false executes; true previews effects and guards without mutation or confirmation. Read-write prompts once when the call contains a global-library lifecycle operation; content-only calls and full mode never prompt. Only previews and prompting calls plan. Unplanned apply returns effects:[] and resolves each item when it runs, so later items may use earlier creations. Planned calls identify objects created by earlier items; a newly created/opened/retrieved global library must be used in a later prompting call. Type kinds: FB, FC and PLC UDT. Type selectors use typeName (optional folderPath; omitted searches the tree, [] means root) or typeGuid; versionNumber, versionGuid or inWork:true selects a version, otherwise its default; set_default_type_version requires versionNumber or versionGuid. create_type converts a plain PLC object; create_type_version requires its linked PLC instance, SimaticML XML content and expectedContentHash from plc_read, with optional replaceInWork. Release takes the in-work version, optional three-part nonzero versionNumber, author and comment; dependencies are not automatically released. Instantiate and synchronize include dependencies. Delete instance scans occur only in previews/planned calls; Portal decides apply-time deletion. Stops at the first failure without rollback or replay. Shared verification trusts completed calls or marks them superseded from resolved identities; type operation results include native read-back evidence where available. Inspect library_read/plc_read before retrying and compile_check after placing instances before reading their content.")]
    public static Task<CallToolResult> LibraryWrite(OpennessWorkerClient workerClient, WriteExecution execution,
        [Description("Ordered library writes, with unique operationId values.")] LibraryWriteItem[] operations,
        [Description("True previews without mutation; omitted or false executes.")] bool dryRun = false,
        McpServer? server = null, CancellationToken cancellationToken = default)
        => execution.RunAsync(new LibraryWriteDomain(workerClient), new WriteCall<LibraryWriteItem>(operations ?? [], dryRun),
            new WriteConfirmationContext(server is null ? null : UserConfirmation.For(server, TimeSpan.FromMinutes(2))),
            cancellationToken: cancellationToken);
}
