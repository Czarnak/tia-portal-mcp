using System;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Resolves a <see cref="PlcTypeAddress"/> to a live type group and (optionally) the type itself.
/// Deliberately a mirror of <see cref="BlockTargetResolver"/>: same deterministic walk, same fuzzy
/// fallback, same ambiguity refusal — resolved against type groups instead of block groups.
/// </summary>
internal static class PlcTypeTargetResolver
{
    public static ResolvedTypeTarget ResolveForExport(Project project, PlcTypeAddress address)
    {
        PlcSoftware plcSoftware = PlcSoftwareLocator.Find(project, address.PlcName);

        if (address.IsDeterministic)
        {
            var group = ResolveDeterministicTypeGroup(plcSoftware, address);
            var type = group.Types.Find(address.TypeName)
                ?? throw new InvalidOperationException($"PLC data type '{address.TypeName}' was not found at '{address.ToDisplayPath()}'.");

            return new ResolvedTypeTarget(group, type, address.TypeName);
        }

        var matches = FindLegacyMatches(plcSoftware, address.TypeName);
        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"PLC data type '{address.TypeName}' not found.");
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(AmbiguousPathMessage(address.TypeName));
        }

        return matches[0];
    }

    public static ResolvedTypeTarget ResolveForImport(Project project, PlcTypeAddress address)
    {
        PlcSoftware plcSoftware = PlcSoftwareLocator.Find(project, address.PlcName);

        if (address.IsDeterministic)
        {
            var group = ResolveDeterministicTypeGroup(plcSoftware, address);
            var existing = group.Types.Find(address.TypeName);
            return new ResolvedTypeTarget(group, existing, address.TypeName);
        }

        var matches = FindLegacyMatches(plcSoftware, address.TypeName);
        if (matches.Count > 1)
        {
            throw new InvalidOperationException(AmbiguousPathMessage(address.TypeName));
        }

        return matches.Count == 1
            ? matches[0]
            : new ResolvedTypeTarget(plcSoftware.TypeGroup, type: null, address.TypeName);
    }

    private static string AmbiguousPathMessage(string typeName)
    {
        return $"PLC data type '{typeName}' is ambiguous. Use the deterministic Path from browse_project_tree, for example 'PLC/Types/.../Type' or 'PLC/Units/Unit/Types/.../Type'.";
    }

    private static PlcTypeGroup ResolveDeterministicTypeGroup(PlcSoftware plcSoftware, PlcTypeAddress address)
    {
        PlcTypeGroup rootGroup = address.UsesSoftwareUnit
            ? FindSoftwareUnit(plcSoftware, address.UnitName!).TypeGroup
            : plcSoftware.TypeGroup;

        return FindTypeGroup(rootGroup, address.FolderPath);
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

    private static PlcTypeGroup FindTypeGroup(PlcTypeGroup rootGroup, IReadOnlyList<string> folderPath)
    {
        PlcTypeGroup current = rootGroup;

        foreach (var folderName in folderPath)
        {
            PlcTypeGroup? next = null;
            foreach (PlcTypeUserGroup childGroup in current.Groups)
            {
                if (string.Equals(childGroup.Name, folderName, StringComparison.OrdinalIgnoreCase))
                {
                    next = childGroup;
                    break;
                }
            }

            current = next ?? throw new InvalidOperationException($"Type folder '{folderName}' not found.");
        }

        return current;
    }

    private static List<ResolvedTypeTarget> FindLegacyMatches(PlcSoftware plcSoftware, string typeName)
    {
        var matches = new List<ResolvedTypeTarget>();
        CollectMatches(plcSoftware.TypeGroup, typeName, matches);

        PlcUnitProvider? unitProvider = plcSoftware.GetService<PlcUnitProvider>();
        if (unitProvider is not null)
        {
            foreach (PlcUnit unit in unitProvider.UnitGroup.Units)
            {
                CollectMatches(unit.TypeGroup, typeName, matches);
            }
        }

        return matches;
    }

    private static void CollectMatches(PlcTypeGroup group, string typeName, List<ResolvedTypeTarget> matches)
    {
        var type = group.Types.Find(typeName);
        if (type is not null)
        {
            matches.Add(new ResolvedTypeTarget(group, type, typeName));
        }

        foreach (PlcTypeUserGroup childGroup in group.Groups)
        {
            CollectMatches(childGroup, typeName, matches);
        }
    }
}

internal sealed class ResolvedTypeTarget
{
    public ResolvedTypeTarget(PlcTypeGroup group, PlcType? type, string documentName)
    {
        Group = group;
        Type = type;
        DocumentName = documentName;
    }

    public PlcTypeGroup Group { get; }

    public PlcType? Type { get; }

    public string DocumentName { get; }

    /// <summary>
    /// GenerateBlocksFromSource targets a PlcTypeUserGroup; the root PlcTypeSystemGroup is not one.
    /// Live test L1.1 exists to establish whether the root case needs the parameterless overload.
    /// </summary>
    public PlcTypeUserGroup? UserGroup => Group as PlcTypeUserGroup;
}
