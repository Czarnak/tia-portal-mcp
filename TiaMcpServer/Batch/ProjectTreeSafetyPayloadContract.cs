using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;

namespace TiaMcpServer.Batch;

/// <summary>
/// Strict host-side decoder for the narrow project-tree snapshots that protect block and block
/// group writes. Deserialization establishes shape only; these validators establish meaning.
/// </summary>
internal static class ProjectTreeSafetyPayloadContract
{
    private const string PlcScopeKind = "Plc";
    private const string SoftwareUnitScopeKind = "SoftwareUnit";
    private const string UserBlockGroupKind = "UserBlockGroup";

    private static readonly string[] AllowedKinds =
    [
        UserBlockGroupKind,
        "FB",
        "FC",
        "OB",
        "GlobalDB",
        "InstanceDB",
        "ArrayDB"
    ];

    internal static string DecodeCreateBlockAndCanonicalize(string payload)
    {
        var snapshot = Deserialize<CreateBlockSafetySnapshotInfo>(payload);
        ValidateCreateBlockSnapshot(snapshot, payload);
        return CanonicalJson.Serialize(snapshot);
    }

    internal static string DecodeCreateBlockGroupAndCanonicalize(string payload)
    {
        var snapshot = Deserialize<CreateBlockGroupSafetySnapshotInfo>(payload);
        ValidateCreateBlockGroupSnapshot(snapshot, payload);
        return CanonicalJson.Serialize(snapshot);
    }

    internal static string DecodeDeleteBlockGroupAndCanonicalize(string payload)
    {
        var snapshot = Deserialize<DeleteBlockGroupSafetySnapshotInfo>(payload);
        ValidateDeleteBlockGroupSnapshot(snapshot, payload);
        return CanonicalJson.Serialize(snapshot);
    }

    private static T Deserialize<T>(string payload)
    {
        if (payload is null)
        {
            throw new JsonException("The worker snapshot payload is required.");
        }

        return CanonicalJson.Deserialize<T>(payload);
    }

    private static void ValidateCreateBlockSnapshot(CreateBlockSafetySnapshotInfo snapshot, string payload)
    {
        var owner = ValidateOwner(snapshot.Owner, payload);
        ValidateParentAndAncestors(owner, snapshot.ParentPath, snapshot.Ancestors, payload);
        ValidateOccupancies(owner, snapshot.ParentPath, snapshot.Occupancies, payload);
        var blockOccupancies = snapshot.Occupancies
            .Where(occupancy => occupancy is not null && IsBlockKind(occupancy.Kind))
            .ToArray();
        if (blockOccupancies.Length > 1)
        {
            throw new JsonException("'occupancies' must contain at most one block occupancy.");
        }

        if (blockOccupancies.Length == 0)
        {
            if (snapshot.OccupiedBlock is not null)
            {
                throw new JsonException("'occupiedBlock' requires a corresponding block occupancy.");
            }

            return;
        }

        ValidateBlockExport(owner, snapshot.ParentPath, snapshot.OccupiedBlock, "occupiedBlock");
        var blockOccupancy = blockOccupancies[0];
        if (!string.Equals(blockOccupancy.Kind, snapshot.OccupiedBlock!.BlockKind, StringComparison.Ordinal)
            || !string.Equals(blockOccupancy.Name, snapshot.OccupiedBlock.Name, StringComparison.Ordinal)
            || !string.Equals(blockOccupancy.Path, snapshot.OccupiedBlock.Path, StringComparison.Ordinal))
        {
            throw new JsonException("'occupiedBlock' must correspond to the declared block occupancy.");
        }
    }

    private static void ValidateCreateBlockGroupSnapshot(CreateBlockGroupSafetySnapshotInfo snapshot, string payload)
    {
        var owner = ValidateOwner(snapshot.Owner, payload);
        ValidateParentAndAncestors(owner, snapshot.ParentPath, snapshot.Ancestors, payload);
        ValidateOccupancies(owner, snapshot.ParentPath, snapshot.Occupancies, payload);
    }

    private static void ValidateDeleteBlockGroupSnapshot(DeleteBlockGroupSafetySnapshotInfo snapshot, string payload)
    {
        var owner = ValidateOwner(snapshot.Owner, payload);
        ValidateParentAndAncestors(owner, snapshot.ParentPath, snapshot.Ancestors, payload);
        RequirePathInOwnerScope(snapshot.GroupPath, owner, "groupPath");
        RequireDirectChildPath(snapshot.ParentPath, snapshot.GroupPath, "groupPath");
        var descendants = RequireNonNullCollection(snapshot.Descendants, "descendants");
        foreach (var descendant in descendants)
        {
            ValidateDescendant(owner, snapshot.GroupPath, descendant, "descendants[]");
        }
    }

    private static ProjectTreeOwnerScopeInfo ValidateOwner(ProjectTreeOwnerScopeInfo? owner, string payload)
    {
        _ = payload;
        if (owner is null)
        {
            throw new JsonException("'owner' is required.");
        }

        RequireText(owner.ScopeKind, "owner.scopeKind");
        RequireText(owner.PlcName, "owner.plcName");
        RequirePath(owner.RootBlocksPath, "owner.rootBlocksPath");

        if (owner.ScopeKind is not PlcScopeKind and not SoftwareUnitScopeKind)
        {
            throw new JsonException("'owner.scopeKind' must be 'Plc' or 'SoftwareUnit'.");
        }

        if (owner.ScopeKind == SoftwareUnitScopeKind)
        {
            RequireText(owner.SoftwareUnitName, "owner.softwareUnitName");
        }
        else if (owner.SoftwareUnitName is not null)
        {
            throw new JsonException("'owner.softwareUnitName' must be null for PLC scope.");
        }

        var expectedRoot = owner.ScopeKind == PlcScopeKind
            ? $"{owner.PlcName}/Blocks"
            : $"{owner.PlcName}/Units/{owner.SoftwareUnitName}/Blocks";
        if (!string.Equals(owner.RootBlocksPath, expectedRoot, StringComparison.Ordinal))
        {
            throw new JsonException("'owner.rootBlocksPath' does not match the declared owner scope.");
        }

        return owner;
    }

    private static void ValidateParentAndAncestors(
        ProjectTreeOwnerScopeInfo owner,
        string parentPath,
        IReadOnlyList<ProjectTreeAncestorInfo>? ancestors,
        string payload)
    {
        _ = payload;
        RequirePathInOwnerScope(parentPath, owner, "parentPath");
        var validatedAncestors = RequireNonNullCollection(ancestors, "ancestors");
        var precedingPath = owner.RootBlocksPath;
        foreach (var ancestor in validatedAncestors)
        {
            ValidateAncestor(owner, ancestor, "ancestors[]");
            RequireDirectChildPath(precedingPath, ancestor!.Path, "ancestors[].path");
            precedingPath = ancestor.Path;
        }

        if (!string.Equals(precedingPath, parentPath, StringComparison.Ordinal))
        {
            throw new JsonException("'ancestors' must be the complete ordered chain to 'parentPath'.");
        }
    }

    private static void ValidateAncestor(ProjectTreeOwnerScopeInfo owner, ProjectTreeAncestorInfo? ancestor, string member)
    {
        if (ancestor is null)
        {
            throw new JsonException($"'{member}' must not contain null.");
        }

        RequireText(ancestor.Name, $"{member}.name");
        RequirePathInOwnerScope(ancestor.Path, owner, $"{member}.path");
        RequireKind(ancestor.Kind, $"{member}.kind");
        if (!string.Equals(ancestor.Kind, UserBlockGroupKind, StringComparison.Ordinal))
        {
            throw new JsonException($"'{member}.kind' must be '{UserBlockGroupKind}'.");
        }

        RequirePathTerminalName(ancestor.Path, ancestor.Name, member);
    }

    private static void ValidateOccupancies(
        ProjectTreeOwnerScopeInfo owner,
        string parentPath,
        IReadOnlyList<ProjectTreeOccupancyInfo>? occupancies,
        string payload)
    {
        _ = payload;
        var validatedOccupancies = RequireNonNullCollection(occupancies, "occupancies");
        foreach (var occupancy in validatedOccupancies)
        {
            ValidateOccupancy(owner, parentPath, occupancy, "occupancies[]");
        }
    }

    private static void ValidateOccupancy(
        ProjectTreeOwnerScopeInfo owner,
        string parentPath,
        ProjectTreeOccupancyInfo? occupancy,
        string member)
    {
        if (occupancy is null)
        {
            throw new JsonException($"'{member}' must not contain null.");
        }

        RequireKind(occupancy.Kind, $"{member}.kind");
        RequireText(occupancy.Name, $"{member}.name");
        RequirePathInOwnerScope(occupancy.Path, owner, $"{member}.path");
        RequireDirectChildPath(parentPath, occupancy.Path, $"{member}.path");
        RequirePathTerminalName(occupancy.Path, occupancy.Name, member);
    }

    private static void ValidateBlockExport(
        ProjectTreeOwnerScopeInfo owner,
        string parentPath,
        ProjectTreeBlockExportInfo? export,
        string member)
    {
        if (export is null)
        {
            throw new JsonException($"'{member}' is required.");
        }

        RequireText(export.Name, $"{member}.name");
        RequirePathInOwnerScope(export.Path, owner, $"{member}.path");
        RequireDirectChildPath(parentPath, export.Path, $"{member}.path");
        RequirePathTerminalName(export.Path, export.Name, member);
        RequireBlockKind(export.BlockKind, $"{member}.blockKind");
        if (!string.Equals(export.Format, "xml", StringComparison.Ordinal))
        {
            throw new JsonException($"'{member}.format' must be 'xml'.");
        }

        RequireText(export.ContentSha256, $"{member}.contentSha256");
        RequireText(export.Content, $"{member}.content");
    }

    private static void ValidateDescendant(
        ProjectTreeOwnerScopeInfo owner,
        string groupPath,
        ProjectTreeGroupDescendantInfo? descendant,
        string member)
    {
        if (descendant is null)
        {
            throw new JsonException($"'{member}' must not contain null.");
        }

        RequireKind(descendant.Kind, $"{member}.kind");
        RequireText(descendant.Name, $"{member}.name");
        RequirePathInOwnerScope(descendant.Path, owner, $"{member}.path");
        RequireDirectChildPath(groupPath, descendant.Path, $"{member}.path");
        RequirePathTerminalName(descendant.Path, descendant.Name, member);
        var children = RequireNonNullCollection(descendant.Children, $"{member}.children");

        if (string.Equals(descendant.Kind, UserBlockGroupKind, StringComparison.Ordinal))
        {
            if (descendant.ContentSha256 is not null || descendant.Content is not null)
            {
                throw new JsonException($"'{member}' group nodes must not carry block content.");
            }
        }
        else
        {
            RequireText(descendant.ContentSha256, $"{member}.contentSha256");
            RequireText(descendant.Content, $"{member}.content");
            if (children.Count != 0)
            {
                throw new JsonException($"'{member}' block nodes must not have children.");
            }
        }

        foreach (var child in children)
        {
            ValidateDescendant(owner, descendant.Path, child, $"{member}.children[]");
        }
    }

    private static void RequirePathInOwnerScope(string? path, ProjectTreeOwnerScopeInfo owner, string member)
    {
        RequirePath(path, member);
        if (!string.Equals(path, owner.RootBlocksPath, StringComparison.Ordinal)
            && !path!.StartsWith(owner.RootBlocksPath + "/", StringComparison.Ordinal))
        {
            throw new JsonException($"'{member}' is outside the declared owner scope.");
        }
    }

    private static void RequireDirectChildPath(string parentPath, string childPath, string member)
    {
        if (!childPath.StartsWith(parentPath + "/", StringComparison.Ordinal))
        {
            throw new JsonException($"'{member}' is not a descendant of its parent path.");
        }

        var childName = childPath[(parentPath.Length + 1)..];
        if (childName.Contains('/', StringComparison.Ordinal))
        {
            throw new JsonException($"'{member}' must be a direct child of its parent path.");
        }
    }

    private static void RequirePathTerminalName(string path, string name, string member)
    {
        if (!path.EndsWith('/' + name, StringComparison.Ordinal))
        {
            throw new JsonException($"'{member}.path' must end in its declared name.");
        }
    }

    private static void RequirePath(string? value, string member)
    {
        RequireText(value, member);
        var segments = value!.Split('/');
        if (segments.Length < 2
            || segments.Any(segment => string.IsNullOrWhiteSpace(segment)
                || string.Equals(segment, ".", StringComparison.Ordinal)
                || string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            throw new JsonException($"'{member}' must use a deterministic slash-separated project tree path.");
        }
    }

    private static void RequireKind(string? kind, string member)
    {
        RequireText(kind, member);
        if (!AllowedKinds.Contains(kind, StringComparer.Ordinal))
        {
            throw new JsonException($"'{member}' has unsupported kind '{kind}'.");
        }
    }

    private static void RequireBlockKind(string? kind, string member)
    {
        RequireKind(kind, member);
        if (!IsBlockKind(kind))
        {
            throw new JsonException($"'{member}' must identify a block, not a group.");
        }
    }

    private static bool IsBlockKind(string? kind)
        => kind is not null && !string.Equals(kind, UserBlockGroupKind, StringComparison.Ordinal);

    private static IReadOnlyList<T> RequireNonNullCollection<T>(IReadOnlyList<T>? value, string member)
    {
        if (value is null)
        {
            throw new JsonException($"'{member}' is required.");
        }

        return value;
    }

    private static void RequireText(string? value, string member)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException($"'{member}' is required.");
        }
    }
}
