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
        var preflight = BlockWritePreflight.PrepareCreate(blockPath, blockType, language);
        var address = preflight.Address;
        var plcSoftware = PlcSoftwareLocator.Find(project, address.PlcName);
        var group = ResolveGroupFromAddress(plcSoftware, address);

        var blockName = address.BlockName;
        var normalizedType = preflight.BlockType;
        var normalizedLang = preflight.Language;

        return BlockCreationCoordinator.Execute(
            () =>
            {
                ImportBlockFromXml(group, blockName, normalizedType, normalizedLang, obEventClass);

                return new BlockMutationResultInfo
                {
                    Operation = "create_block",
                    ProjectPath = project.Path.FullName,
                    PlcName = address.PlcName ?? plcSoftware.Name,
                    BlockPath = blockPath,
                    BlockType = normalizedType,
                    Language = (normalizedType is "GLOBALDB" or "DB") ? null : normalizedLang
                };
            },
            () => VerifyCreatedBlockPostconditions(project, address, blockPath));
    }

    private static BlockPostconditionEvidence VerifyCreatedBlockPostconditions(
        Project project,
        BlockAddress address,
        string blockPath)
    {
        try
        {
            _ = BlockTargetResolver.ResolveForExport(project, address);
        }
        catch (Exception exception)
        {
            return new BlockPostconditionEvidence(
                compileSucceeded: false,
                reExportSucceeded: false,
                diagnosticMessage: "Created block could not be resolved: " + exception.Message);
        }

        try
        {
            var report = CompileChecker.Compile(project, address.PlcName, blockPath);
            if (report.TotalErrorCount != 0
                || string.Equals(report.OverallState, "Error", StringComparison.OrdinalIgnoreCase))
            {
                return new BlockPostconditionEvidence(
                    compileSucceeded: false,
                    reExportSucceeded: true,
                    diagnosticMessage: "Compilation reported errors after block creation.");
            }
        }
        catch (Exception exception)
        {
            return new BlockPostconditionEvidence(
                compileSucceeded: false,
                reExportSucceeded: true,
                diagnosticMessage: "Compilation could not complete after block creation: " + exception.Message);
        }

        return new BlockPostconditionEvidence(
            compileSucceeded: true,
            reExportSucceeded: true,
            diagnosticMessage: "Created block resolved and compiled successfully.");
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
        var rootGroup = address.IsDeterministic
            ? BlockTargetResolver.ResolveOwnerForDeterministicPath(plcSoftware, address).RootBlockGroup
            : plcSoftware.BlockGroup;
        var group = FindUserGroupByPath(rootGroup, allSegments)
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

        var owner = BlockTargetResolver.ResolveOwnerForDeterministicPath(plcSoftware, address);
        return BlockTargetResolver.FindBlockGroup(owner.RootBlockGroup, address.FolderPath);
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
