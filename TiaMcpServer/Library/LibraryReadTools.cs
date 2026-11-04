using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Library;

[McpServerToolType]
public class LibraryReadTools
{
    private const string ToolName = "library_read";
    internal const string ContractVersion = "1.0";

    [McpServerTool(
        Name = ToolName,
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(OperationBatchReadResponse))]
    [Description("Read TIA Portal libraries with up to 50 independent operations. Needs a verified project binding (bind_project first); never opens, retrieves, saves, closes or changes a library. Global libraries are found only among those already open in the bound Portal. Operations: list_global_libraries (no selectors); inspect_library (scope, plus libraryPath or libraryName for global); list_children (scope, library, folderKind types|masterCopies, optional folderPath); list_versions (scope, library, typeGuid, optional folderPath); list_dependencies (scope, library, typeGuid, versionGuid, optional direction dependencies|dependents, optional folderPath). scope is project (the bound project's own library; standalone projects only) or global. folderPath is a list of exact folder names. Paged operations (all except inspect_library) take offset (default 0) and limit (1-200, default 100) and return a page { items, total, offset, nextOffset, nextCursor, complete, diagnostics }. While more rows exist nextCursor is set; pass it as cursor (not with offset) with the same operation and selectors to continue the same frozen row set; the server keeps only the 4 most recent library snapshots for 10 minutes, so an older cursor may fail snapshot_unavailable: restart without a cursor. Rows are read live per page; a flag that could not be read is null with a diagnostic, never false. Reads run independently, so a failing item does not stop later operations. A result larger than the budget is omitted whole with guidance; lower the limit or re-run that operationId in its own library_read call.")]
    public static Task<CallToolResult> LibraryRead(
        OpennessWorkerClient workerClient,
        LibraryPageCursorCodec cursorCodec,
        [Description("Ordered library read operations, each with unique operationId and operation.")] LibraryReadItem[] operations,
        CancellationToken cancellationToken = default)
        => BatchReadTool.RunAsync(workerClient, operations, ToolName, ContractVersion,
            LibraryReadCatalog.ValidateRead, LibraryReadCatalog.ValidateAccessMode,
            async operation =>
            {
                var result = await LibraryWorkerInvoker.InvokeReadAsync(workerClient, cursorCodec, operation, cancellationToken).ConfigureAwait(false);
                // The binding is captured after the call, like hmi_read, so a cursor is sealed to the binding that served the page.
                return LibraryPayloadContract.Project(operation, result, cursorCodec, workerClient.BindingSnapshot);
            },
            RetryGuidance, cancellationToken);

    internal static StructuredOperationBatch ApplyBudget(
        StructuredOperationBatch batch,
        int? maxItemChars = null,
        int? maxDocumentChars = null)
        => BatchReadTool.ApplyBudget(batch, ToolName, ContractVersion, RetryGuidance, maxItemChars, maxDocumentChars);

    private static string RetryGuidance(StructuredOperationItem item)
        => LibraryReadCatalog.IsPaged(item.Operation)
            ? $"Lower limit, or re-run this operationId in its own {ToolName} call."
            : $"Re-run this operationId in its own {ToolName} call; for one value above the limit, raise --max-item-chars and --max-document-chars.";

    internal static CallToolResult Error(string category, string message)
        => BatchReadTool.Error(ToolName, ContractVersion, category, message);
}
