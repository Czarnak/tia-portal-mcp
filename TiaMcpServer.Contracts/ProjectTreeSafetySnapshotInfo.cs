using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts;

public sealed record ProjectTreeOwnerScopeInfo(
    [property: JsonRequired] string ScopeKind,
    [property: JsonRequired] string PlcName,
    string? SoftwareUnitName,
    [property: JsonRequired] string RootBlocksPath);

public sealed record ProjectTreeAncestorInfo(
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Path,
    [property: JsonRequired] string Kind);

public sealed record ProjectTreeOccupancyInfo(
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Path);

public sealed record ProjectTreeBlockExportInfo(
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Path,
    [property: JsonRequired] string BlockKind,
    [property: JsonRequired] string Format,
    [property: JsonRequired] string ContentSha256,
    [property: JsonRequired] string Content);

public sealed record ProjectTreeGroupDescendantInfo(
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Path,
    string? ContentSha256,
    string? Content,
    [property: JsonRequired] IReadOnlyList<ProjectTreeGroupDescendantInfo> Children);

public sealed record CreateBlockSafetySnapshotInfo(
    [property: JsonRequired] ProjectTreeOwnerScopeInfo Owner,
    [property: JsonRequired] string ParentPath,
    [property: JsonRequired] IReadOnlyList<ProjectTreeAncestorInfo> Ancestors,
    [property: JsonRequired] IReadOnlyList<ProjectTreeOccupancyInfo> Occupancies,
    ProjectTreeBlockExportInfo? OccupiedBlock);

public sealed record CreateBlockGroupSafetySnapshotInfo(
    [property: JsonRequired] ProjectTreeOwnerScopeInfo Owner,
    [property: JsonRequired] string ParentPath,
    [property: JsonRequired] IReadOnlyList<ProjectTreeAncestorInfo> Ancestors,
    [property: JsonRequired] IReadOnlyList<ProjectTreeOccupancyInfo> Occupancies);

public sealed record DeleteBlockGroupSafetySnapshotInfo(
    [property: JsonRequired] ProjectTreeOwnerScopeInfo Owner,
    [property: JsonRequired] string ParentPath,
    [property: JsonRequired] string GroupPath,
    [property: JsonRequired] IReadOnlyList<ProjectTreeAncestorInfo> Ancestors,
    [property: JsonRequired] IReadOnlyList<ProjectTreeGroupDescendantInfo> Descendants);
