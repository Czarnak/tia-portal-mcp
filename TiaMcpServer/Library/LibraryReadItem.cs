using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Library;

/// <summary>Strict request shape for one library_read operation; cross-field rules live in the catalog.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LibraryReadItem : IOperationBatchItem
{
    [Description("Client-supplied unique identifier for this library operation; returned results are keyed by it.")]
    public string OperationId { get; set; } = string.Empty;

    [Description("Library operation to run: list_global_libraries, inspect_library, list_children, list_versions, list_dependencies.")]
    public string Operation { get; set; } = string.Empty;

    [Description("project or global. Required by inspect_library, list_children, list_versions and list_dependencies; not valid for list_global_libraries. project reads the bound project's own library; global reads a library already open in the bound Portal.")]
    public string? Scope { get; set; }

    [Description("Global scope only (inspect_library, list_children, list_versions, list_dependencies): absolute path (drive or UNC) of an already-open global library, compared case-insensitively. Give this or libraryName; when both are given both must match.")]
    public string? LibraryPath { get; set; }

    [Description("Global scope only (inspect_library, list_children, list_versions, list_dependencies): exact (ordinal) name of an already-open global library. Give this or libraryPath; when both are given both must match.")]
    public string? LibraryName { get; set; }

    [Description("inspect_library only: set true to include metadata for an already-open global library. Omitted or false skips metadata getters; project scope accepts only omitted or false.")]
    public bool? IncludeMetadata { get; set; }

    [Description("list_children only: types or masterCopies.")]
    public string? FolderKind { get; set; }

    [Description("Exact folder names below the root, at most 32 nonblank segments (not a slash-delimited string). list_children: omit for the root. list_versions and list_dependencies: omit to search the whole type tree; supply an empty list to select only root types, or a path to select only that folder.")]
    public List<string>? FolderPath { get; set; }

    [Description("list_versions and list_dependencies: exact ordinal library type name. Supply this or typeGuid; folderPath optionally narrows the search. Multiple matches fail target_ambiguous with candidate paths and GUIDs.")]
    public string? TypeName { get; set; }

    [Description("list_versions and list_dependencies: nonempty GUID of the library type. Supply this or typeName.")]
    public string? TypeGuid { get; set; }

    [Description("list_dependencies only: exact version number with three numeric parts, excluding 0.0.0. Supply this or versionGuid; omit both to use the default version.")]
    public string? VersionNumber { get; set; }

    [Description("list_dependencies only: nonempty GUID of the type version. Supply this or versionNumber; omit both to use the default version.")]
    public string? VersionGuid { get; set; }

    [Description("list_dependencies only: dependencies (default) or dependents.")]
    public string? Direction { get; set; }

    [Description("Paged operations only (list_global_libraries, list_children, list_versions and list_dependencies; not inspect_library): zero-based index of the first row. Default 0. Not valid with cursor.")]
    public int? Offset { get; set; }

    [Description("Paged operations only (list_global_libraries, list_children, list_versions and list_dependencies; not inspect_library): rows per page, 1 to 200. Default 100.")]
    public int? Limit { get; set; }

    [Description("Paged operations only (list_global_libraries, list_children, list_versions and list_dependencies; not inspect_library): nextCursor from a previous call with the same operation and selectors; continues that snapshot. Not valid with offset.")]
    public string? Cursor { get; set; }
}
