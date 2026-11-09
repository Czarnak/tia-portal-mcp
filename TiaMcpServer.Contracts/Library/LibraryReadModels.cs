using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts.Library;

/// <summary>
/// Parameters of one <c>library_read</c> operation as forwarded to the worker. The host has already
/// validated them. <see cref="SnapshotId"/> is worker-only: the host sets it from a sealed cursor and
/// never exposes it publicly.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LibraryQueryInfo
{
    public string? Scope { get; set; }
    public string? LibraryPath { get; set; }
    public string? LibraryName { get; set; }
    public bool? IncludeMetadata { get; set; }
    public string? FolderKind { get; set; }
    public List<string>? FolderPath { get; set; }
    public string? TypeName { get; set; }
    public string? TypeGuid { get; set; }
    public string? VersionNumber { get; set; }
    public string? VersionGuid { get; set; }
    public string? Direction { get; set; }
    public int? Offset { get; set; }
    public int? Limit { get; set; }
    public string? SnapshotId { get; set; }
}

/// <summary>Exact selectors; folder paths are segments, never slash-delimited names.</summary>
public sealed class LibraryIdentityInfo
{
    public string Scope { get; set; } = string.Empty;
    public string? LibraryPath { get; set; }
    public string? LibraryName { get; set; }
    public string? FolderKind { get; set; }
    public List<string> FolderPath { get; set; } = new();
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? TypeGuid { get; set; }
    public string? VersionGuid { get; set; }
}

public sealed class LibraryEntryInfo
{
    /// <summary>Present only for global inspect_library with includeMetadata:true.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LibraryMetadataInfo? Metadata { get; set; }
    public LibraryIdentityInfo Identity { get; set; } = new();
    public string? Namespace { get; set; }
    public string? VersionNumber { get; set; }
    public string? State { get; set; }
    public bool? IsDefault { get; set; }
    public bool? IsReadOnly { get; set; }
    public bool? IsWriteProtected { get; set; }
    public bool? IsModified { get; set; }
    /// <summary>
    /// Library rows of <c>list_global_libraries</c> and of global <c>inspect_library</c>: <c>system</c>, <c>corporate</c>
    /// or <c>user</c>; null (with a row diagnostic) when unknown or unmatched. Null on project-library and every non-library row.
    /// </summary>
    public string? LibraryType { get; set; }
    /// <summary>
    /// Library rows of <c>list_global_libraries</c> (whether open in the bound Portal) and of global <c>inspect_library</c>
    /// (always true). Null on project-library and every non-library row.
    /// </summary>
    public bool? IsOpen { get; set; }
    public string? NativeType { get; set; }
    public List<string> Diagnostics { get; set; } = new();
}

public sealed class LibraryPageInfo
{
    public List<LibraryEntryInfo> Items { get; set; } = new();
    public int Total { get; set; }
    public int Offset { get; set; }
    public int? NextOffset { get; set; }
    public string? SnapshotId { get; set; }
    public bool Complete { get; set; }
    public List<string> Diagnostics { get; set; } = new();
}

public sealed class LibraryMetadataInfo
{
    public string? Author { get; set; }
    public string? Version { get; set; }
    public string? Family { get; set; }
    public string? Copyright { get; set; }
    public string? LastModifiedBy { get; set; }
    public string? CreationTime { get; set; }
    public string? LastModified { get; set; }
    public long? SizeKilobytes { get; set; }
    public List<LibraryCommentInfo>? Comments { get; set; }
    public List<string>? SupportedLanguages { get; set; }
    public List<string>? ActiveLanguages { get; set; }
    public string? EditingLanguage { get; set; }
    public string? ReferenceLanguage { get; set; }
    public List<LibraryUsedProductInfo>? UsedProducts { get; set; }
    public List<LibraryHistoryEntryInfo>? History { get; set; }
    public bool Complete { get; set; }
    public List<string> Diagnostics { get; set; } = new();
}

public sealed class LibraryCommentInfo
{
    public string? Culture { get; set; }
    public string? Text { get; set; }
}

public sealed class LibraryUsedProductInfo
{
    public string? Name { get; set; }
    public string? Version { get; set; }
}

public sealed class LibraryHistoryEntryInfo
{
    public string? DateTime { get; set; }
    public string? Text { get; set; }
}
