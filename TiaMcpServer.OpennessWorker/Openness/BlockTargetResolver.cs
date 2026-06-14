using System;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class BlockTargetResolver
{
    public static ResolvedBlockTarget ResolveForExport(Project project, BlockAddress address)
    {
        PlcSoftware plcSoftware = PlcSoftwareLocator.Find(project, address.PlcName);

        if (address.IsDeterministic)
        {
            var group = ResolveDeterministicBlockGroup(plcSoftware, address);
            var block = group.Blocks.Find(address.BlockName)
                ?? throw new InvalidOperationException($"Block '{address.BlockName}' was not found at '{address.ToDisplayPath()}'.");

            return new ResolvedBlockTarget(group, block, address.BlockName);
        }

        var matches = FindLegacyMatches(plcSoftware, address.BlockName);
        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"Block '{address.BlockName}' not found.");
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Block '{address.BlockName}' is ambiguous. Use the deterministic Path from browse_project_tree, for example 'PLC/Blocks/.../Block' or 'PLC/Units/Unit/Blocks/.../Block'.");
        }

        return matches[0];
    }

    public static ResolvedBlockTarget ResolveForImport(Project project, BlockAddress address)
    {
        PlcSoftware plcSoftware = PlcSoftwareLocator.Find(project, address.PlcName);

        if (address.IsDeterministic)
        {
            var group = ResolveDeterministicBlockGroup(plcSoftware, address);
            var existing = group.Blocks.Find(address.BlockName);
            return new ResolvedBlockTarget(group, existing, address.BlockName);
        }

        var matches = FindLegacyMatches(plcSoftware, address.BlockName);
        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Block '{address.BlockName}' is ambiguous. Use the deterministic Path from browse_project_tree, for example 'PLC/Blocks/.../Block' or 'PLC/Units/Unit/Blocks/.../Block'.");
        }

        return matches.Count == 1
            ? matches[0]
            : new ResolvedBlockTarget(plcSoftware.BlockGroup, block: null, address.BlockName);
    }

    private static PlcBlockGroup ResolveDeterministicBlockGroup(PlcSoftware plcSoftware, BlockAddress address)
    {
        PlcBlockGroup rootGroup = address.UsesSoftwareUnit
            ? FindSoftwareUnit(plcSoftware, address.UnitName!).BlockGroup
            : plcSoftware.BlockGroup;

        return FindBlockGroup(rootGroup, address.FolderPath);
    }

    private static PlcUnit FindSoftwareUnit(PlcSoftware plcSoftware, string unitName)
    {
        PlcUnitProvider? unitProvider = plcSoftware.GetService<PlcUnitProvider>();
        if (unitProvider is null)
        {
            throw new InvalidOperationException($"PLC software '{plcSoftware.Name}' does not expose software units.");
        }

        foreach (PlcUnit unit in unitProvider.UnitGroup.Units)
        {
            if (string.Equals(unit.Name, unitName, StringComparison.OrdinalIgnoreCase))
            {
                return unit;
            }
        }

        throw new InvalidOperationException($"Software Unit '{unitName}' not found in PLC software '{plcSoftware.Name}'.");
    }

    private static PlcBlockGroup FindBlockGroup(PlcBlockGroup rootGroup, IReadOnlyList<string> folderPath)
    {
        PlcBlockGroup current = rootGroup;

        foreach (var folderName in folderPath)
        {
            PlcBlockGroup? next = null;
            foreach (PlcBlockGroup childGroup in current.Groups)
            {
                if (string.Equals(childGroup.Name, folderName, StringComparison.OrdinalIgnoreCase))
                {
                    next = childGroup;
                    break;
                }
            }

            current = next ?? throw new InvalidOperationException($"Block folder '{folderName}' not found.");
        }

        return current;
    }

    private static List<ResolvedBlockTarget> FindLegacyMatches(PlcSoftware plcSoftware, string blockName)
    {
        var matches = new List<ResolvedBlockTarget>();
        CollectMatches(plcSoftware.BlockGroup, blockName, matches);

        PlcUnitProvider? unitProvider = plcSoftware.GetService<PlcUnitProvider>();
        if (unitProvider is not null)
        {
            foreach (PlcUnit unit in unitProvider.UnitGroup.Units)
            {
                CollectMatches(unit.BlockGroup, blockName, matches);
            }
        }

        return matches;
    }

    private static void CollectMatches(PlcBlockGroup group, string blockName, List<ResolvedBlockTarget> matches)
    {
        var block = group.Blocks.Find(blockName);
        if (block is not null)
        {
            matches.Add(new ResolvedBlockTarget(group, block, blockName));
        }

        foreach (PlcBlockGroup childGroup in group.Groups)
        {
            CollectMatches(childGroup, blockName, matches);
        }
    }
}

internal sealed class ResolvedBlockTarget
{
    public ResolvedBlockTarget(PlcBlockGroup group, PlcBlock? block, string documentName)
    {
        Group = group;
        Block = block;
        DocumentName = documentName;
    }

    public PlcBlockGroup Group { get; }

    public PlcBlock? Block { get; }

    public string DocumentName { get; }
}
