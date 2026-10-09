using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

using Project = Siemens.Engineering.Project;

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
        var obClass = normalizedType == "OB" ? ResolveObEventClass(obEventClass) : null;

        // The plan saw no occupant, so anything found now means the project changed since.
        PlcWritePreconditions.RequireBlockAbsent(group, blockName);
        PlcWritePreconditions.RequireCpuNameFree(plcSoftware, blockName, currentName: null);
        var obNumber = obClass is null ? (int?)null : PickObNumber(plcSoftware, obClass);

        return BlockCreationCoordinator.Execute(
            () =>
            {
                var block = ImportBlockFromXml(group, blockName, normalizedType, normalizedLang, obClass?.Name, obNumber);

                return new BlockMutationResultInfo
                {
                    Operation = "create_block",
                    ProjectPath = project.Path.FullName,
                    PlcName = address.PlcName ?? plcSoftware.Name,
                    BlockPath = blockPath,
                    BlockType = normalizedType,
                    Language = (normalizedType is "GLOBALDB" or "DB") ? null : normalizedLang,
                    Number = block.Number,
                    ObEventClass = obClass?.Name
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
            throw new WorkerOperationException(
                WorkerFailureCategories.TargetNotFound,
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
            ?? throw new WorkerOperationException(
                WorkerFailureCategories.TargetNotFound,
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

    private static ObEventClass ResolveObEventClass(string? name)
    {
        if (ObEventClasses.TryGet(name ?? ObEventClasses.Default, out var cls))
        {
            return cls;
        }

        throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError,
            $"Unknown obEventClass '{name}'. Valid values: {ObEventClasses.NamesForMessage()}.");
    }

    // Re-applies the number rule against the project as it is now, not as it was planned.
    private static int PickObNumber(PlcSoftware plcSoftware, ObEventClass cls)
    {
        var used = ObNumberScanner.Collect(plcSoftware);
        if (ObNumberRules.Pick(cls, used) is { } number)
        {
            return number;
        }

        if (cls.IsSingleton)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.StateChanged,
                $"An OB already holds number {cls.BaseNumber}, the only number of event class '{cls.Name}' in PLC '{plcSoftware.Name}'. The project changed after the write was planned.");
        }

        throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError,
            $"No OB number from {ObNumberRules.FirstFree} to {ObNumberRules.Max} is free in PLC '{plcSoftware.Name}'.");
    }

    private static PlcBlock ImportBlockFromXml(
        PlcBlockGroup group,
        string blockName,
        string blockType,
        string language,
        string? obEventClass,
        int? obNumber)
    {
        var xml = BlockSourceGenerator.Generate(blockName, blockType, language, obEventClass, obNumber);
        BlockSourceValidator.Validate(blockType, language, xml);
        var tempFile = Path.Combine(
            Path.GetTempPath(),
            $"tia-mcp-create-{Guid.NewGuid():N}.xml");

        try
        {
            File.WriteAllText(tempFile, xml, System.Text.Encoding.UTF8);
            foreach (PlcBlock block in group.Blocks.Import(new FileInfo(tempFile), ImportOptions.None))
            {
                return block;
            }

            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                $"Importing block '{blockName}' returned no block.");
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
