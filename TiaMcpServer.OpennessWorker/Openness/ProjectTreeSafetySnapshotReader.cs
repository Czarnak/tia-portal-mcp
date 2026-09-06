using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class ProjectTreeSafetySnapshotReader
{
    private const string UserBlockGroupKind = "UserBlockGroup";

    public static CreateBlockSafetySnapshotInfo ReadCreateBlockSnapshot(
        Project project,
        string blockPath,
        string blockType,
        string? language,
        string? obEventClass)
    {
        _ = blockType;
        _ = language;
        _ = obEventClass;

        var context = ResolveContext(project, blockPath);
        var occupancies = ReadOccupancies(context.ParentGroup, context.ParentPath, context.Address.BlockName);
        var occupiedBlock = context.ParentGroup.Blocks.Find(context.Address.BlockName);

        return new CreateBlockSafetySnapshotInfo(
            ToOwnerInfo(context.Owner),
            context.ParentPath,
            context.Ancestors,
            occupancies,
            occupiedBlock is null
                ? null
                : ExportBlock(project, occupiedBlock, CombinePath(context.ParentPath, occupiedBlock.Name)));
    }

    public static CreateBlockGroupSafetySnapshotInfo ReadCreateBlockGroupSnapshot(
        Project project,
        string blockPath)
    {
        var context = ResolveContext(project, blockPath);
        return new CreateBlockGroupSafetySnapshotInfo(
            ToOwnerInfo(context.Owner),
            context.ParentPath,
            context.Ancestors,
            ReadOccupancies(context.ParentGroup, context.ParentPath, context.Address.BlockName));
    }

    public static DeleteBlockGroupSafetySnapshotInfo ReadDeleteBlockGroupSnapshot(
        Project project,
        string blockPath)
    {
        var context = ResolveContext(project, blockPath);
        var targetGroup = FindUserGroup(context.ParentGroup, context.Address.BlockName)
            ?? throw new InvalidOperationException(
                $"Block group '{context.Address.BlockName}' was not found at '{context.Address.ToDisplayPath()}'.");
        var groupPath = CombinePath(context.ParentPath, targetGroup.Name);

        return new DeleteBlockGroupSafetySnapshotInfo(
            ToOwnerInfo(context.Owner),
            context.ParentPath,
            groupPath,
            context.Ancestors,
            ReadDescendants(project, targetGroup, groupPath));
    }

    private static SnapshotContext ResolveContext(Project project, string blockPath)
    {
        var address = BlockAddress.Parse(blockPath);
        if (!address.IsDeterministic)
        {
            throw new InvalidOperationException(
                "Project-tree safety snapshots require deterministic block paths.");
        }

        var plcSoftware = PlcSoftwareLocator.Find(project, address.PlcName);
        var owner = BlockTargetResolver.ResolveOwnerForDeterministicPath(plcSoftware, address);
        var parentGroup = BlockTargetResolver.FindBlockGroup(owner.RootBlockGroup, address.FolderPath);
        var parentPath = address.FolderPath.Count == 0
            ? owner.RootBlocksPath
            : $"{owner.RootBlocksPath}/{string.Join("/", address.FolderPath)}";

        return new SnapshotContext(
            address,
            owner,
            parentGroup,
            parentPath,
            ReadAncestors(owner.RootBlocksPath, address.FolderPath));
    }

    private static IReadOnlyList<ProjectTreeAncestorInfo> ReadAncestors(
        string rootBlocksPath,
        IReadOnlyList<string> folderPath)
    {
        var ancestors = new List<ProjectTreeAncestorInfo>(folderPath.Count);
        var path = rootBlocksPath;
        foreach (var name in folderPath)
        {
            path = CombinePath(path, name);
            ancestors.Add(new ProjectTreeAncestorInfo(name, path, UserBlockGroupKind));
        }

        return ancestors;
    }

    private static IReadOnlyList<ProjectTreeOccupancyInfo> ReadOccupancies(
        PlcBlockGroup parentGroup,
        string parentPath,
        string requestedName)
    {
        var occupancies = new List<ProjectTreeOccupancyInfo>();
        var block = parentGroup.Blocks.Find(requestedName);
        if (block is not null)
        {
            occupancies.Add(new ProjectTreeOccupancyInfo(
                BlockKindName(block),
                block.Name,
                CombinePath(parentPath, block.Name)));
        }

        var group = FindUserGroup(parentGroup, requestedName);
        if (group is not null)
        {
            occupancies.Add(new ProjectTreeOccupancyInfo(
                UserBlockGroupKind,
                group.Name,
                CombinePath(parentPath, group.Name)));
        }

        return occupancies
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<ProjectTreeGroupDescendantInfo> ReadDescendants(
        Project project,
        PlcBlockGroup parentGroup,
        string parentPath)
    {
        var descendants = new List<ProjectTreeGroupDescendantInfo>();

        foreach (PlcBlock block in parentGroup.Blocks)
        {
            var blockPath = CombinePath(parentPath, block.Name);
            var export = ExportBlock(project, block, blockPath);
            descendants.Add(new ProjectTreeGroupDescendantInfo(
                export.BlockKind,
                export.Name,
                export.Path,
                export.ContentSha256,
                export.Content,
                Array.Empty<ProjectTreeGroupDescendantInfo>()));
        }

        foreach (PlcBlockUserGroup group in parentGroup.Groups)
        {
            var groupPath = CombinePath(parentPath, group.Name);
            descendants.Add(new ProjectTreeGroupDescendantInfo(
                UserBlockGroupKind,
                group.Name,
                groupPath,
                ContentSha256: null,
                Content: null,
                ReadDescendants(project, group, groupPath)));
        }

        return descendants
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ToArray();
    }

    private static ProjectTreeBlockExportInfo ExportBlock(
        Project project,
        PlcBlock block,
        string blockPath)
    {
        var content = BlockExporter.Export(project, blockPath, SourceFormatNames.Xml);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException($"Block export for '{blockPath}' was empty.");
        }

        return new ProjectTreeBlockExportInfo(
            block.Name,
            blockPath,
            BlockKindName(block),
            SourceFormatNames.Xml,
            ComputeSha256(content),
            content);
    }

    private static ProjectTreeOwnerScopeInfo ToOwnerInfo(ResolvedBlockOwner owner)
        => new ProjectTreeOwnerScopeInfo(
            owner.ScopeKind,
            owner.PlcName,
            owner.SoftwareUnitName,
            owner.RootBlocksPath);

    private static PlcBlockUserGroup? FindUserGroup(PlcBlockGroup parentGroup, string name)
    {
        foreach (PlcBlockUserGroup group in parentGroup.Groups)
        {
            if (string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return group;
            }
        }

        return null;
    }

    private static string BlockKindName(PlcBlock block) => block switch
    {
        GlobalDB => "GlobalDB",
        InstanceDB => "InstanceDB",
        ArrayDB => "ArrayDB",
        OB => "OB",
        FB => "FB",
        FC => "FC",
        _ => block.GetType().Name
    };

    private static string ComputeSha256(string value)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value)))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static string CombinePath(string parentPath, string name)
        => parentPath + "/" + name;

    private sealed class SnapshotContext
    {
        public SnapshotContext(
            BlockAddress address,
            ResolvedBlockOwner owner,
            PlcBlockGroup parentGroup,
            string parentPath,
            IReadOnlyList<ProjectTreeAncestorInfo> ancestors)
        {
            Address = address;
            Owner = owner;
            ParentGroup = parentGroup;
            ParentPath = parentPath;
            Ancestors = ancestors;
        }

        public BlockAddress Address { get; }

        public ResolvedBlockOwner Owner { get; }

        public PlcBlockGroup ParentGroup { get; }

        public string ParentPath { get; }

        public IReadOnlyList<ProjectTreeAncestorInfo> Ancestors { get; }
    }
}
