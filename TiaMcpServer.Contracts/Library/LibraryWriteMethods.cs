namespace TiaMcpServer.Contracts.Library;

/// <summary>Reachable worker methods and closed selectors for library_write.</summary>
public static class LibraryWriteMethods
{
    public const string GlobalPlan = "library_global_plan";
    public const string ProjectPlan = "library_project_plan";
    public const string ArchiveExtension = ".zal21";

    public static string[] LifecycleOperations { get; } =
        { "create_global", "open_global", "retrieve_global", "save_global", "close_global", "archive_global" };

    public static string[] Operations { get; } = LifecycleOperations.Concat(new[]
    {
        "create_folder", "delete_folder", "create_master_copy", "copy_master_copy", "instantiate_master_copy", "delete_master_copy"
    }).ToArray();

    public static string[] GlobalLibraryWrites { get; } = Operations
        .Where(operation => operation is not ("create_master_copy" or "instantiate_master_copy"))
        .Select(operation => "library_global_" + operation).ToArray();

    public static string[] ProjectLibraryWrites { get; } = Operations.Skip(LifecycleOperations.Length)
        .Select(operation => "library_project_" + operation).ToArray();

    public static string[] All { get; } = GlobalLibraryWrites.Concat(ProjectLibraryWrites)
        .Concat(new[] { GlobalPlan, ProjectPlan }).ToArray();

    public static bool TouchesProject(LibraryWriteRequestInfo item)
        => item.Scope == "project" || item.SourceScope == "project"
            || item.Operation is "create_master_copy" or "instantiate_master_copy";

    public static string ForItem(LibraryWriteRequestInfo item)
        => "library_" + (TouchesProject(item) ? "project" : "global") + "_" + item.Operation;

    public static string PlanFor(LibraryWriteRequestInfo item) => TouchesProject(item) ? ProjectPlan : GlobalPlan;

    public static string? OperationFor(string method)
        => !All.Contains(method, StringComparer.Ordinal) ? null
            : method.StartsWith("library_project_", StringComparison.Ordinal) ? "library_project_write" : "library_write";

    public static string? Validate(LibraryWriteRequestInfo item)
    {
        var allowed = item.Operation switch
        {
            "create_global" => "directory name",
            "open_global" => "filePath openMode",
            "retrieve_global" => "filePath directory openMode",
            "save_global" => "scope libraryPath libraryName",
            "close_global" => "scope libraryPath libraryName closeAction",
            "archive_global" => "scope libraryPath libraryName directory name",
            "create_folder" or "delete_folder" => "scope libraryPath libraryName folderKind folderPath name",
            "create_master_copy" => "scope libraryPath libraryName folderPath plcName plcKind plcFolderPath plcObjectName",
            "copy_master_copy" => "scope libraryPath libraryName folderPath sourceScope sourceLibraryPath sourceLibraryName sourceFolderPath name",
            "instantiate_master_copy" => "scope libraryPath libraryName folderPath name plcName plcKind plcFolderPath",
            "delete_master_copy" => "scope libraryPath libraryName folderPath name",
            _ => null
        };
        if (allowed is null) return "Unknown library write operation.";
        var members = allowed.Split(' ');
        foreach (var member in new (string Name, object? Value)[]
        {
            ("scope", item.Scope), ("libraryPath", item.LibraryPath), ("libraryName", item.LibraryName),
            ("folderKind", item.FolderKind), ("folderPath", item.FolderPath), ("name", item.Name),
            ("sourceScope", item.SourceScope), ("sourceLibraryPath", item.SourceLibraryPath),
            ("sourceLibraryName", item.SourceLibraryName), ("sourceFolderPath", item.SourceFolderPath),
            ("directory", item.Directory), ("filePath", item.FilePath), ("openMode", item.OpenMode),
            ("closeAction", item.CloseAction), ("plcName", item.PlcName), ("plcKind", item.PlcKind),
            ("plcFolderPath", item.PlcFolderPath), ("plcObjectName", item.PlcObjectName)
        })
            if (member.Value is not null && !members.Contains(member.Name, StringComparer.Ordinal))
                return $"{member.Name} is not valid for {item.Operation}.";

        if (members.Contains("scope"))
        {
            if (LifecycleOperations.Contains(item.Operation) && item.Scope != "global") return "scope must be global.";
            if (LibrarySelectorRules.Check(item.Scope, item.LibraryPath, item.LibraryName, item.FolderPath, "") is { } error)
                return error;
        }
        if (item.Operation == "copy_master_copy"
            && LibrarySelectorRules.Check(item.SourceScope, item.SourceLibraryPath, item.SourceLibraryName, item.SourceFolderPath, "source") is { } sourceError)
            return sourceError;
        if (members.Contains("name") && string.IsNullOrWhiteSpace(item.Name)) return "name must be nonblank.";
        if (members.Contains("directory") && !LibraryReadMethods.IsAbsolutePath(item.Directory)) return "directory must be absolute.";
        if (members.Contains("filePath"))
        {
            var extension = item.Operation == "open_global" ? ".al21" : ArchiveExtension;
            if (!LibraryReadMethods.IsAbsolutePath(item.FilePath)
                || !item.FilePath!.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return $"filePath must be absolute and end with {extension}.";
        }
        if (members.Contains("openMode") && item.OpenMode is not ("readOnly" or "readWrite")) return "openMode must be readOnly or readWrite.";
        if (members.Contains("closeAction") && item.CloseAction is not ("save" or "discard")) return "closeAction must be save or discard.";
        if (members.Contains("folderKind") && item.FolderKind is not ("types" or "masterCopies")) return "folderKind must be types or masterCopies.";
        if (members.Contains("plcName"))
        {
            if (string.IsNullOrWhiteSpace(item.PlcName)) return "plcName must be nonblank.";
            if (item.PlcFolderPath is { Count: > 32 } || item.PlcFolderPath?.Any(string.IsNullOrWhiteSpace) == true)
                return "plcFolderPath must contain at most 32 nonblank name segments.";
            if (item.Operation == "create_master_copy")
            {
                if (item.PlcKind is not ("block" or "plcType" or "tagTable")) return "plcKind must be block, plcType or tagTable.";
                if (string.IsNullOrWhiteSpace(item.PlcObjectName)) return "plcObjectName must be nonblank.";
            }
            else if (item.PlcKind is not ("blockGroup" or "typeGroup" or "tagTableGroup"))
                return "plcKind must be blockGroup, typeGroup or tagTableGroup.";
        }
        return null;
    }
}
