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
        var plcSoftware = PlcSoftwareLocator.FindUnique(project, address.PlcName).Software;
        var group = ResolveGroupFromAddress(plcSoftware, address);

        var blockName = address.BlockName;
        var normalizedType = preflight.BlockType;
        var normalizedLang = preflight.Language;

        // The plan saw no occupant, so anything found now means the project changed since.
        PlcWritePreconditions.RequireBlockAbsent(group, blockName);
        PlcWritePreconditions.RequireCpuNameFree(plcSoftware, blockName, currentName: null);

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
        var plcSoftware = PlcSoftwareLocator.FindUnique(project, address.PlcName).Software;
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
        var plcSoftware = PlcSoftwareLocator.FindUnique(project, address.PlcName).Software;
        var parentGroup = ResolveGroupFromAddress(plcSoftware, address);

        PlcWritePreconditions.RequireGroupNameFree(parentGroup, address.BlockName);
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
        var plcSoftware = PlcSoftwareLocator.FindUnique(project, address.PlcName).Software;

        // The group to delete is FolderPath + BlockName
        var allSegments = new List<string>(address.FolderPath) { address.BlockName };
        var rootGroup = ResolveRootGroup(plcSoftware, address);
        var group = FindUserGroupByPath(rootGroup, allSegments)
            ?? throw new InvalidOperationException(
                $"Block group '{address.BlockName}' was not found at '{address.ToDisplayPath()}'.");

        PlcWritePreconditions.RequireReadableDescendants(group);
        group.Delete();

        return new BlockMutationResultInfo
        {
            Operation = "delete_block_group",
            ProjectPath = project.Path.FullName,
            PlcName = address.PlcName ?? plcSoftware.Name,
            BlockPath = blockPath
        };
    }

    // Resolves the parent group that a new block/group would be created inside. A deterministic path
    // names it; the two-segment 'PLC/Name' means the PLC root group; a bare name is not accepted.
    internal static PlcBlockGroup ResolveGroupFromAddress(PlcSoftware plcSoftware, BlockAddress address)
    {
        var owner = ResolveRootGroup(plcSoftware, address);
        return address.IsDeterministic
            ? BlockTargetResolver.FindBlockGroup(owner, address.FolderPath)
            : owner;
    }

    private static PlcBlockGroup ResolveRootGroup(PlcSoftware plcSoftware, BlockAddress address)
    {
        if (address.IsDeterministic)
        {
            return BlockTargetResolver.ResolveOwnerForDeterministicPath(plcSoftware, address).RootBlockGroup;
        }

        if (address.PlcName is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "The block path must name the PLC: use 'PLC/Name' for the PLC root group or "
                + "'PLC/Blocks/.../Name' for a folder.");
        }

        return plcSoftware.BlockGroup;
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
            group.Blocks.Import(new FileInfo(tempFile), ImportOptions.None);
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
