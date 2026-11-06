using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts.Library;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LibraryWriteRequestInfo
{
    public string Operation { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Scope { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? LibraryPath { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? LibraryName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? FolderKind { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public List<string>? FolderPath { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Name { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? SourceScope { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? SourceLibraryPath { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? SourceLibraryName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public List<string>? SourceFolderPath { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Directory { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? FilePath { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? OpenMode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? CloseAction { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? PlcName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? PlcKind { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public List<string>? PlcFolderPath { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? PlcObjectName { get; set; }
}

public sealed class LibraryWritePlanInfo
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public LibraryIdentityInfo? Library { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public LibraryIdentityInfo? Target { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? AlreadyOpen { get; set; }
    public bool MasterCopyExists { get; set; }
    public List<LibraryIdentityInfo> FolderContents { get; set; } = new();
    public bool ContentsTruncated { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Unverifiable { get; set; }
}

public sealed class LibraryWriteResultInfo
{
    public LibraryIdentityInfo Library { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public LibraryIdentityInfo? Target { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? AlreadyOpen { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ArchivePath { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ActualName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? Saved { get; set; }
}

public sealed class LibraryWriteOutcomeInfo
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? Saved { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ActualName { get; set; }
}
