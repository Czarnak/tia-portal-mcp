using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Library;

/// <summary>Host-side validation and item-to-worker-query mapping for library_read.</summary>
public static class LibraryReadCatalog
{
    public const int DefaultLimit = 100;
    private const string InspectLibrary = "inspect_library";

    public static bool IsOperation(string? operation)
        => operation is not null && LibraryReadMethods.Operations.Contains(operation, StringComparer.Ordinal);

    /// <summary>Every operation except inspect_library returns a page.</summary>
    public static bool IsPaged(string? operation)
        => IsOperation(operation) && !string.Equals(operation, InspectLibrary, StringComparison.Ordinal);

    public static OperationValidation ValidateRead(IReadOnlyList<LibraryReadItem>? operations)
    {
        if (OperationBatchRules.CheckCount(operations?.Count ?? 0) is { } countError)
            return OperationValidation.Invalid(WorkerFailureCategories.ValidationError, countError);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var item in operations!)
        {
            if (OperationBatchRules.CheckItem(item, ids) is { } itemError) { errors.Add(itemError); continue; }
            if (Check(item) is { } error) errors.Add($"Operation '{item.OperationId}': {error}");
        }
        return errors.Count == 0 ? OperationValidation.Valid
            : OperationValidation.Invalid(WorkerFailureCategories.ValidationError, string.Join("\n", errors));
    }

    public static IReadOnlyList<string> ValidateAccessMode(IReadOnlyList<LibraryReadItem> operations, McpAccessMode mode)
        => operations
            .Where(item => IsOperation(item.Operation)
                && !OperationPolicyCatalog.IsAllowed(mode, LibraryReadMethods.ForOperation(item.Operation, item.Scope)))
            .Select(item => $"Operation '{item.Operation}' is not permitted in {McpAccessModeNames.ToName(mode)} mode.")
            .ToArray();

    /// <summary>First rule violation of one item, or null. The invoker repeats it so a direct call cannot skip it.</summary>
    public static string? Check(LibraryReadItem item)
    {
        if (!IsOperation(item.Operation)) return $"Unknown library read operation '{item.Operation}'.";
        if (item.Cursor is not null && !IsPaged(item.Operation)) return "cursor is only valid for paged operations.";
        if (item.Cursor is not null && item.Offset is not null) return "cursor is not valid with offset; the cursor carries the position.";
        return LibraryReadMethods.Validate(item.Operation, ToQuery(item));
    }

    /// <summary>The worker query; paged operations get concrete paging, inspect_library none.</summary>
    public static LibraryQueryInfo ToQuery(LibraryReadItem item)
    {
        var paged = IsPaged(item.Operation);
        return new LibraryQueryInfo
        {
            Scope = item.Scope,
            LibraryPath = item.LibraryPath,
            LibraryName = item.LibraryName,
            IncludeMetadata = item.IncludeMetadata,
            FolderKind = item.FolderKind,
            FolderPath = item.FolderPath,
            TypeName = item.TypeName,
            TypeGuid = item.TypeGuid,
            VersionNumber = item.VersionNumber,
            VersionGuid = item.VersionGuid,
            Direction = item.Direction,
            Offset = paged ? item.Offset ?? 0 : item.Offset,
            Limit = paged ? item.Limit ?? DefaultLimit : item.Limit,
        };
    }
}
