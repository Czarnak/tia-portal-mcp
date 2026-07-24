using System;
using System.Collections.Generic;
using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class BlockMutationService
{
    public static BlockMutationResultInfo CreateBlock(
        Project project,
        string blockPath,
        string blockType,
        string? language,
        string? obEventClass)
    {
        var address = BlockAddress.Parse(blockPath);
        var plcSoftware = PlcSoftwareLocator.Find(project, address.PlcName);
        var group = ResolveGroupFromAddress(plcSoftware, address);

        var blockName = address.BlockName;
        var normalizedType = blockType.ToUpperInvariant();
        var normalizedLang = (language ?? "LAD").ToUpperInvariant();

        if (normalizedType is "FB" or "FC" or "OB" or "GLOBALDB" or "DB")
        {
            ImportBlockFromXml(group, blockName, normalizedType, normalizedLang, obEventClass);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unknown block type '{blockType}'. Valid types: FB, FC, OB, GlobalDB.");
        }

        return new BlockMutationResultInfo
        {
            Operation = "create_block",
            ProjectPath = project.Path.FullName,
            PlcName = address.PlcName ?? plcSoftware.Name,
            BlockPath = blockPath,
            BlockType = normalizedType,
            Language = (normalizedType is "GLOBALDB" or "DB") ? null : normalizedLang
        };
    }

    public static BlockMutationResultInfo DeleteBlock(Project project, string blockPath)
    {
        var address = BlockAddress.Parse(blockPath);
        var plcSoftware = PlcSoftwareLocator.Find(project, address.PlcName);
        var target = BlockTargetResolver.ResolveForExport(project, address);

        if (target.Block is null)
        {
            throw new InvalidOperationException(
                $"Block '{address.BlockName}' was not found at '{address.ToDisplayPath()}'.");
        }

        target.Block.Delete();

        return new BlockMutationResultInfo
        {
            Operation = "delete_block",
            ProjectPath = project.Path.FullName,
            PlcName = address.PlcName ?? plcSoftware.Name,
            BlockPath = blockPath
        };
    }

    public static BlockMutationResultInfo CreateBlockGroup(Project project, string blockPath)
    {
        var address = BlockAddress.Parse(blockPath);
        var plcSoftware = PlcSoftwareLocator.Find(project, address.PlcName);
        var parentGroup = ResolveGroupFromAddress(plcSoftware, address);

        parentGroup.Groups.Create(address.BlockName);

        return new BlockMutationResultInfo
        {
            Operation = "create_block_group",
            ProjectPath = project.Path.FullName,
            PlcName = address.PlcName ?? plcSoftware.Name,
            BlockPath = blockPath
        };
    }

    public static BlockMutationResultInfo DeleteBlockGroup(Project project, string blockPath)
    {
        var address = BlockAddress.Parse(blockPath);
        var plcSoftware = PlcSoftwareLocator.Find(project, address.PlcName);

        // The group to delete is FolderPath + BlockName
        var allSegments = new List<string>(address.FolderPath) { address.BlockName };
        var group = FindUserGroupByPath(plcSoftware.BlockGroup, allSegments)
            ?? throw new InvalidOperationException(
                $"Block group '{address.BlockName}' was not found at '{address.ToDisplayPath()}'.");

        group.Delete();

        return new BlockMutationResultInfo
        {
            Operation = "delete_block_group",
            ProjectPath = project.Path.FullName,
            PlcName = address.PlcName ?? plcSoftware.Name,
            BlockPath = blockPath
        };
    }

    // Resolves the parent group that a new block/group would be created inside.
    private static PlcBlockGroup ResolveGroupFromAddress(PlcSoftware plcSoftware, BlockAddress address)
    {
        if (!address.IsDeterministic)
        {
            return plcSoftware.BlockGroup;
        }

        return FindGroupByPath(plcSoftware.BlockGroup, address.FolderPath)
            ?? throw new InvalidOperationException(
                $"Block group path '{string.Join("/", address.FolderPath)}' was not found.");
    }

    private static PlcBlockGroup? FindGroupByPath(PlcBlockGroup root, IEnumerable<string> path)
    {
        PlcBlockGroup current = root;
        foreach (var segment in path)
        {
            PlcBlockGroup? next = null;
            foreach (PlcBlockGroup child in current.Groups)
            {
                if (string.Equals(child.Name, segment, StringComparison.OrdinalIgnoreCase))
                {
                    next = child;
                    break;
                }
            }

            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    // Same traversal as FindGroupByPath but typed as PlcBlockUserGroup so Delete() is available.
    private static PlcBlockUserGroup? FindUserGroupByPath(PlcBlockGroup root, IEnumerable<string> path)
    {
        PlcBlockGroup current = root;
        PlcBlockUserGroup? result = null;
        foreach (var segment in path)
        {
            PlcBlockUserGroup? next = null;
            foreach (PlcBlockUserGroup child in current.Groups)
            {
                if (string.Equals(child.Name, segment, StringComparison.OrdinalIgnoreCase))
                {
                    next = child;
                    break;
                }
            }

            if (next is null)
            {
                return null;
            }

            result = next;
            current = next;
        }

        return result;
    }

    private static void ImportBlockFromXml(
        PlcBlockGroup group,
        string blockName,
        string blockType,
        string language,
        string? obEventClass)
    {
        var xml = BlockSourceGenerator.Generate(blockName, blockType, language, obEventClass);
        BlockSourceValidator.Validate(blockType, language, xml);
        var tempFile = Path.Combine(
            Path.GetTempPath(),
            $"tia-mcp-create-{Guid.NewGuid():N}.xml");

        try
        {
            File.WriteAllText(tempFile, xml, System.Text.Encoding.UTF8);
            group.Blocks.Import(new FileInfo(tempFile), ImportOptions.Override);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

}
