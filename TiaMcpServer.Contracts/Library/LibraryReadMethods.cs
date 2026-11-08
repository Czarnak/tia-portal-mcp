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
                || query.TypeName is not null || query.VersionNumber is not null || query.VersionGuid is not null || query.Direction is not null
                ? "list_global_libraries accepts only paging selectors." : null;

        if (LibrarySelectorRules.Check(query.Scope, query.LibraryPath, query.LibraryName, query.FolderPath, "") is { } selectorError)
            return selectorError;

        switch (operation)
        {
            case "inspect_library":
                return query.FolderKind is not null || query.FolderPath is not null || query.TypeGuid is not null
                    || query.TypeName is not null || query.VersionNumber is not null || query.VersionGuid is not null || query.Direction is not null || query.Offset is not null || query.Limit is not null
                    ? "inspect_library accepts only library selectors." : null;
            case "list_children":
                if (query.FolderKind is not ("types" or "masterCopies")) return "list_children requires folderKind types or masterCopies.";
                return query.TypeName is not null || query.TypeGuid is not null || query.VersionNumber is not null || query.VersionGuid is not null || query.Direction is not null
                    ? "Type/version selectors are not valid for list_children." : null;
            default:
                return ValidateVersionRead(operation, query);
        }
    }

    private static string? ValidateVersionRead(string operation, LibraryQueryInfo query)
    {
        if (LibrarySelectorRules.CheckType(query.TypeName, query.TypeGuid, query.FolderPath) is { } error) return error;
        if (query.FolderKind is not null) return "Version reads use type folders; omit folderKind.";
        if (operation == "list_versions")
            return query.VersionNumber is not null || query.VersionGuid is not null || query.Direction is not null
                ? "list_versions accepts no version selectors or direction." : null;
        if (LibrarySelectorRules.CheckVersion(query.VersionNumber, query.VersionGuid, null, false) is { } versionError) return versionError;
        return query.Direction is not (null or "dependencies" or "dependents")
            ? "direction must be dependencies or dependents." : null;
    }
}
