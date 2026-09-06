using System;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Resolves a <see cref="BlockAddress"/> to a live block group and (optionally) the block itself.
///
/// <para>
/// It carries the owning external source group out with the result. A PLC and each of its software
/// units own a separate <c>PlcExternalSourceSystemGroup</c>, and only the walk that found the block
/// knows which one it went through. Re-deriving that afterwards from the <see cref="PlcBlock"/>
/// alone cannot distinguish a unit-scoped block from a PLC-scoped one, and getting it wrong sends
/// the external-source round trip into the wrong software context — silently writing a stray new
/// block into the top-level PLC instead of updating the addressed one.
/// </para>
/// </summary>
internal static class BlockTargetResolver
{
    public static ResolvedBlockTarget ResolveForExport(Project project, BlockAddress address)
    {
        PlcSoftware plcSoftware = PlcSoftwareLocator.Find(project, address.PlcName);

        if (address.IsDeterministic)
        {
            var owner = ResolveOwnerForDeterministicPath(plcSoftware, address);
            var group = FindBlockGroup(owner.RootBlockGroup, address.FolderPath);
            var block = group.Blocks.Find(address.BlockName)
                ?? throw new InvalidOperationException($"Block '{address.BlockName}' was not found at '{address.ToDisplayPath()}'.");

            return new ResolvedBlockTarget(owner.ExternalSourceGroup, group, block, address.BlockName);
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
            var owner = ResolveOwnerForDeterministicPath(plcSoftware, address);
            var group = FindBlockGroup(owner.RootBlockGroup, address.FolderPath);
            var existing = group.Blocks.Find(address.BlockName);
            return new ResolvedBlockTarget(owner.ExternalSourceGroup, group, existing, address.BlockName);
        }

        var matches = FindLegacyMatches(plcSoftware, address.BlockName);
        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Block '{address.BlockName}' is ambiguous. Use the deterministic Path from browse_project_tree, for example 'PLC/Blocks/.../Block' or 'PLC/Units/Unit/Blocks/.../Block'.");
        }

        return matches.Count == 1
            ? matches[0]
            : new ResolvedBlockTarget(
                plcSoftware.ExternalSourceGroup,
                plcSoftware.BlockGroup,
                block: null,
                address.BlockName);
    }

    internal static ResolvedBlockOwner ResolveOwnerForDeterministicPath(
        PlcSoftware plcSoftware,
        BlockAddress address)
    {
        if (!address.UsesSoftwareUnit)
        {
            return new ResolvedBlockOwner(
                "Plc",
                plcSoftware.Name,
                address.UnitName,
                $"{plcSoftware.Name}/Blocks",
                plcSoftware.BlockGroup,
                plcSoftware.ExternalSourceGroup);
        }

        PlcUnit unit = FindSoftwareUnit(plcSoftware, address.UnitName!);
        return new ResolvedBlockOwner(
            "SoftwareUnit",
            plcSoftware.Name,
            unit.Name,
            $"{plcSoftware.Name}/Units/{unit.Name}/Blocks",
            unit.BlockGroup,
            unit.ExternalSourceGroup);
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

    internal static PlcBlockGroup FindBlockGroup(
        PlcBlockGroup rootGroup,
        IReadOnlyList<string> folderPath)
        => ResolveBlockGroupPath(rootGroup, folderPath).Group;

    internal static ResolvedBlockGroupPath ResolveBlockGroupPath(
        PlcBlockGroup rootGroup,
        IReadOnlyList<string> folderPath)
    {
        PlcBlockGroup current = rootGroup;
        var resolvedFolderNames = new List<string>(folderPath.Count);

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
            resolvedFolderNames.Add(current.Name);
        }

        return new ResolvedBlockGroupPath(current, resolvedFolderNames.AsReadOnly());
    }

    private static List<ResolvedBlockTarget> FindLegacyMatches(PlcSoftware plcSoftware, string blockName)
    {
        var matches = new List<ResolvedBlockTarget>();

        foreach (var owner in EnumerateOwners(plcSoftware))
        {
            CollectMatches(owner, owner.RootBlockGroup, blockName, matches);
        }

        return matches;
    }

    /// <summary>
    /// The PLC itself, then each of its software units — every scope that owns both a block tree
    /// and its own external source group.
    /// </summary>
    private static IEnumerable<ResolvedBlockOwner> EnumerateOwners(PlcSoftware plcSoftware)
    {
        yield return new ResolvedBlockOwner(
            "Plc",
            plcSoftware.Name,
            softwareUnitName: null,
            $"{plcSoftware.Name}/Blocks",
            plcSoftware.BlockGroup,
            plcSoftware.ExternalSourceGroup);

        PlcUnitProvider? unitProvider = plcSoftware.GetService<PlcUnitProvider>();
        if (unitProvider is null)
        {
            yield break;
        }

        foreach (PlcUnit unit in unitProvider.UnitGroup.Units)
        {
            yield return new ResolvedBlockOwner(
                "SoftwareUnit",
                plcSoftware.Name,
                unit.Name,
                $"{plcSoftware.Name}/Units/{unit.Name}/Blocks",
                unit.BlockGroup,
                unit.ExternalSourceGroup);
        }
    }

    private static void CollectMatches(
        ResolvedBlockOwner owner,
        PlcBlockGroup group,
        string blockName,
        List<ResolvedBlockTarget> matches)
    {
        var block = group.Blocks.Find(blockName);
        if (block is not null)
        {
            matches.Add(new ResolvedBlockTarget(owner.ExternalSourceGroup, group, block, blockName));
        }

        foreach (PlcBlockGroup childGroup in group.Groups)
        {
            CollectMatches(owner, childGroup, blockName, matches);
        }
    }
}

internal sealed class ResolvedBlockGroupPath
{
    public ResolvedBlockGroupPath(
        PlcBlockGroup group,
        IReadOnlyList<string> resolvedFolderNames)
    {
        Group = group;
        ResolvedFolderNames = resolvedFolderNames;
    }

    public PlcBlockGroup Group { get; }

    public IReadOnlyList<string> ResolvedFolderNames { get; }
}

internal sealed class ResolvedBlockOwner
{
    public ResolvedBlockOwner(
        string scopeKind,
        string plcName,
        string? softwareUnitName,
        string rootBlocksPath,
        PlcBlockGroup rootBlockGroup,
        PlcExternalSourceSystemGroup externalSourceGroup)
    {
        ScopeKind = scopeKind;
        PlcName = plcName;
        SoftwareUnitName = softwareUnitName;
        RootBlocksPath = rootBlocksPath;
        RootBlockGroup = rootBlockGroup;
        ExternalSourceGroup = externalSourceGroup;
    }

    public string ScopeKind { get; }

    public string PlcName { get; }

    public string? SoftwareUnitName { get; }

    public string RootBlocksPath { get; }

    public PlcBlockGroup RootBlockGroup { get; }

    public PlcExternalSourceSystemGroup ExternalSourceGroup { get; }
}

internal sealed class ResolvedBlockTarget
{
    public ResolvedBlockTarget(
        PlcExternalSourceSystemGroup externalSourceGroup,
        PlcBlockGroup group,
        PlcBlock? block,
        string documentName)
    {
        ExternalSourceGroup = externalSourceGroup;
        Group = group;
        Block = block;
        DocumentName = documentName;
    }

    /// <summary>
    /// The external source group of the software scope the block actually lives in — the PLC's for
    /// a PLC-scoped block, the unit's own for a unit-scoped one. Both GenerateSource (export) and
    /// CreateFromFile (import) must run against this one, or the round trip silently targets the
    /// wrong software context.
    /// </summary>
    public PlcExternalSourceSystemGroup ExternalSourceGroup { get; }

    public PlcBlockGroup Group { get; }

    public PlcBlock? Block { get; }

    public string DocumentName { get; }

    /// <summary>
    /// GenerateBlocksFromSource targets a PlcBlockUserGroup; the root PlcBlockSystemGroup is not
    /// one, so a block sitting directly under "Program blocks" resolves to null here and takes the
    /// group-less overload.
    /// </summary>
    public PlcBlockUserGroup? UserGroup => Group as PlcBlockUserGroup;
}
