using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Library;

public static class LibraryWriteCatalog
{
    public static OperationValidation ValidateWrite(IReadOnlyList<LibraryWriteItem>? operations)
    {
        if (OperationBatchRules.CheckCount(operations?.Count ?? 0) is { } countError)
            return OperationValidation.Invalid(WorkerFailureCategories.ValidationError, countError);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var item in operations!)
        {
            if (OperationBatchRules.CheckItem(item, ids) is { } itemError) { errors.Add(itemError); continue; }
            if (LibraryWriteMethods.Validate(ToWorkerRequest(item)) is { } error)
                errors.Add($"Operation '{item.Operation}' (operationId '{item.OperationId}'): {error}");
        }
        return errors.Count == 0 ? OperationValidation.Valid
            : OperationValidation.Invalid(WorkerFailureCategories.ValidationError, string.Join("\n", errors));
    }

    public static IReadOnlyList<string> ValidateAccessMode(IReadOnlyList<LibraryWriteItem> operations, McpAccessMode mode)
        => operations.Where(item => item is not null && LibraryWriteMethods.Operations.Contains(item.Operation, StringComparer.Ordinal)
                && !OperationPolicyCatalog.IsAllowed(mode, LibraryWriteMethods.ForItem(ToWorkerRequest(item))))
            .Select(item => $"Operation '{item.Operation}' (operationId '{item.OperationId}') is not permitted in {McpAccessModeNames.ToName(mode)} mode.")
            .ToArray();

    public static LibraryWriteRequestInfo ToWorkerRequest(LibraryWriteItem item) => new()
    {
        OperationId = item.OperationId, Operation = item.Operation, Scope = item.Scope,
        LibraryPath = item.LibraryPath, LibraryName = item.LibraryName, FolderKind = item.FolderKind,
        FolderPath = item.FolderPath, Name = item.Name, SourceScope = item.SourceScope,
        SourceLibraryPath = item.SourceLibraryPath, SourceLibraryName = item.SourceLibraryName,
        SourceFolderPath = item.SourceFolderPath, Directory = item.Directory, FilePath = item.FilePath,
        OpenMode = item.OpenMode, CloseAction = item.CloseAction, PlcName = item.PlcName, PlcKind = item.PlcKind,
        PlcFolderPath = item.PlcFolderPath, PlcObjectName = item.PlcObjectName,
        TypeName = item.TypeName, TypeGuid = item.TypeGuid, VersionNumber = item.VersionNumber,
        VersionGuid = item.VersionGuid, InWork = item.InWork, Author = item.Author, Comment = item.Comment,
        Content = item.Content, ExpectedContentHash = item.ExpectedContentHash, ReplaceInWork = item.ReplaceInWork,
    };
}
