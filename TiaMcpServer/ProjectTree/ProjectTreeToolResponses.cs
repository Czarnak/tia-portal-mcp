using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.ProjectTree;

public static class ProjectTreeContract
{
    public const string Version = "3.0";
    public const int DefaultPageSize = 100;
    public const int MinimumPageSize = 1;
    public const int MaximumPageSize = 200;
    public const int MaximumResponseChars = 60_000;
}

public static class ProjectTreeStatuses
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}

public sealed record ProjectTreeSnapshotMetadata(
    string SnapshotId,
    DateTimeOffset CreatedAt,
    DateTimeOffset IdleExpiresAt,
    int TotalNodes);

public sealed record ProjectTreeQuery(
    string ProjectPath,
    IReadOnlyList<ProjectTreeSelectorSegment>? StartSelector,
    int? Depth);

public sealed record ProjectTreePagination(
    int Offset,
    int RequestedPageSize,
    int ReturnedCount,
    string? NextCursor);

public sealed record ProjectTreeFlatNode(
    string NodeId,
    string? ParentNodeId,
    int Sequence,
    string Name,
    string NodeType,
    IReadOnlyDictionary<string, string> Details);

public sealed record BrowseProjectTreeResult(
    ProjectTreeSnapshotMetadata Snapshot,
    ProjectTreeQuery Query,
    ProjectTreePagination Pagination,
    IReadOnlyList<ProjectTreeFlatNode> Nodes);

public sealed record BrowseProjectTreeFailure(string Category, string Message);

public sealed record BrowseProjectTreeResponse(
    string ContractVersion,
    string Status,
    BrowseProjectTreeResult? Result,
    BrowseProjectTreeFailure? Failure,
    IReadOnlyList<string> Warnings);
