namespace TiaMcpServer.Contracts.Library;

/// <summary>
/// Worker method names and selector rules of <c>library_read</c>. Global reads act on a library
/// already open in the bound Portal; project reads act on the bound project's own library.
/// </summary>
public static class LibraryReadMethods
{
    private const string ListGlobalLibraries = "list_global_libraries";
    private const string ListGlobalLibrariesMethod = "library_list_global_libraries";
    private const string ProjectPrefix = "library_project_";
    private const string GlobalPrefix = "library_global_";
    private const int MaxFolderSegments = 32;
    private const int MaxLimit = 200;

    public static string[] Operations { get; } =
        { ListGlobalLibraries, "inspect_library", "list_children", "list_versions", "list_dependencies" };

    public static string[] ProjectLibraryReads { get; } =
        Operations.Skip(1).Select(operation => ProjectPrefix + operation).ToArray();

    public static string[] GlobalLibraryReads { get; } =
        new[] { ListGlobalLibrariesMethod }.Concat(Operations.Skip(1).Select(operation => GlobalPrefix + operation)).ToArray();

    public static string[] All { get; } = ProjectLibraryReads.Concat(GlobalLibraryReads).ToArray();

    public static string ForOperation(string operation, string? scope)
        => operation == ListGlobalLibraries ? ListGlobalLibrariesMethod : "library_" + scope + "_" + operation;

    public static string? OperationFor(string method)
    {
        if (!All.Contains(method, StringComparer.Ordinal)) return null;
        if (method == ListGlobalLibrariesMethod) return ListGlobalLibraries;
        return method.Substring(method.StartsWith(ProjectPrefix, StringComparison.Ordinal)
            ? ProjectPrefix.Length : GlobalPrefix.Length);
    }

    /// <summary>Drive-rooted (<c>C:\</c> or <c>C:/</c>) or UNC with at least server and share.</summary>
    public static bool IsAbsolutePath(string? path)
        => path is not null
            && (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/')
                || path.StartsWith(@"\\", StringComparison.Ordinal)
                    && path.Substring(2).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Length >= 2);

    /// <summary>Returns the first selector-rule violation, or null. The host wraps the message.</summary>
    public static string? Validate(string operation, LibraryQueryInfo query)
    {
        if (!Operations.Contains(operation, StringComparer.Ordinal)) return "Unknown library read operation.";
        if (query.Offset < 0 || query.Limit is < 1 or > MaxLimit) return "offset must be nonnegative; limit must be 1 to 200.";
        if (operation == ListGlobalLibraries)
            return query.Scope is not null || query.LibraryPath is not null || query.LibraryName is not null
                || query.FolderKind is not null || query.FolderPath is not null || query.TypeGuid is not null
                || query.VersionGuid is not null || query.Direction is not null
                ? "list_global_libraries accepts only paging selectors." : null;

        if (query.Scope is not ("project" or "global")) return "scope must be project or global.";
        if (query.Scope == "project" && (query.LibraryPath is not null || query.LibraryName is not null))
            return "Project library identity comes from the verified project binding, not a library path/name.";
        if (query.Scope == "global" && string.IsNullOrWhiteSpace(query.LibraryPath) && string.IsNullOrWhiteSpace(query.LibraryName))
            return "Global reads require an exact libraryPath or libraryName of an already-open library.";
        if (query.LibraryPath is not null && !IsAbsolutePath(query.LibraryPath)) return "libraryPath must be absolute.";
        if (query.LibraryName is not null && string.IsNullOrWhiteSpace(query.LibraryName)) return "libraryName must be nonblank.";
        if (query.FolderPath is { Count: > MaxFolderSegments } || query.FolderPath?.Any(string.IsNullOrWhiteSpace) == true)
            return "folderPath must contain at most 32 nonblank exact name segments.";

        switch (operation)
        {
            case "inspect_library":
                return query.FolderKind is not null || query.FolderPath is not null || query.TypeGuid is not null
                    || query.VersionGuid is not null || query.Direction is not null || query.Offset is not null || query.Limit is not null
                    ? "inspect_library accepts only library selectors." : null;
            case "list_children":
                if (query.FolderKind is not ("types" or "masterCopies")) return "list_children requires folderKind types or masterCopies.";
                return query.TypeGuid is not null || query.VersionGuid is not null || query.Direction is not null
                    ? "Type/version selectors are not valid for list_children." : null;
            default:
                return ValidateVersionRead(operation, query);
        }
    }

    private static string? ValidateVersionRead(string operation, LibraryQueryInfo query)
    {
        if (!IsNonEmptyGuid(query.TypeGuid)) return "A nonempty typeGuid is required.";
        if (query.FolderKind is not null) return "Version reads use type folders; omit folderKind.";
        if (operation == "list_versions")
            return query.VersionGuid is not null || query.Direction is not null
                ? "list_versions accepts no versionGuid or direction." : null;
        if (!IsNonEmptyGuid(query.VersionGuid)) return "list_dependencies requires a nonempty versionGuid.";
        return query.Direction is not (null or "dependencies" or "dependents")
            ? "direction must be dependencies or dependents." : null;
    }

    private static bool IsNonEmptyGuid(string? value) => Guid.TryParse(value, out var guid) && guid != Guid.Empty;
}
