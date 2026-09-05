using System;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Resolves a <see cref="PlcTypeAddress"/> to a live type group and (optionally) the type itself.
/// Deliberately a mirror of <see cref="BlockTargetResolver"/>: same deterministic walk, same fuzzy
/// fallback, same ambiguity refusal — resolved against type groups instead of block groups.
///
/// <para>
/// One thing it does that the block resolver does not: it carries the owning external source group
/// out with the result. A PLC and each of its software units own a separate
/// <c>PlcExternalSourceSystemGroup</c>, and only the walk that found the type knows which one it
/// went through. Re-deriving that afterwards from the <see cref="PlcType"/> alone cannot
/// distinguish a unit-scoped type from a PLC-scoped one, and getting it wrong sends the whole
/// external-source round trip into the wrong software context.
/// </para>
/// </summary>
internal static class PlcTypeTargetResolver
{
    public static ResolvedTypeTarget ResolveForExport(Project project, PlcTypeAddress address)
    {
        PlcSoftware plcSoftware = PlcSoftwareLocator.Find(project, address.PlcName);

        if (address.IsDeterministic)
        {
            var owner = ResolveDeterministicOwner(plcSoftware, address);
            var group = FindTypeGroup(owner.RootTypeGroup, address.FolderPath);
            var type = group.Types.Find(address.TypeName)
                ?? throw new InvalidOperationException($"PLC data type '{address.TypeName}' was not found at '{address.ToDisplayPath()}'.");

            return new ResolvedTypeTarget(owner.ExternalSourceGroup, group, type, address.TypeName);
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
            var owner = ResolveDeterministicOwner(plcSoftware, address);
            var group = FindTypeGroup(owner.RootTypeGroup, address.FolderPath);
            var existing = group.Types.Find(address.TypeName);

            return new ResolvedTypeTarget(owner.ExternalSourceGroup, group, existing, address.TypeName);
        }

        var matches = FindLegacyMatches(plcSoftware, address.TypeName);
        if (matches.Count > 1)
        {
            throw new InvalidOperationException(AmbiguousPathMessage(address.TypeName));
        }

        return matches.Count == 1
            ? matches[0]
            : new ResolvedTypeTarget(
                plcSoftware.ExternalSourceGroup,
                plcSoftware.TypeGroup,
                type: null,
                address.TypeName);
    }

    private static string AmbiguousPathMessage(string typeName)
    {
        return $"PLC data type '{typeName}' is ambiguous. Use the deterministic Path from browse_project_tree, for example 'PLC/Types/.../Type' or 'PLC/Units/Unit/Types/.../Type'.";
    }

    private static TypeOwner ResolveDeterministicOwner(PlcSoftware plcSoftware, PlcTypeAddress address)
    {
        if (!address.UsesSoftwareUnit)
        {
            return new TypeOwner(plcSoftware.TypeGroup, plcSoftware.ExternalSourceGroup);
        }

        PlcUnit unit = FindSoftwareUnit(plcSoftware, address.UnitName!);
        return new TypeOwner(unit.TypeGroup, unit.ExternalSourceGroup);
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

        foreach (var owner in EnumerateOwners(plcSoftware))
        {
            CollectMatches(owner, owner.RootTypeGroup, typeName, matches);
        }

        return matches;
    }

    /// <summary>
    /// The PLC itself, then each of its software units — every scope that owns both a type tree and
    /// its own external source group.
    /// </summary>
    private static IEnumerable<TypeOwner> EnumerateOwners(PlcSoftware plcSoftware)
    {
        yield return new TypeOwner(plcSoftware.TypeGroup, plcSoftware.ExternalSourceGroup);

        PlcUnitProvider? unitProvider = plcSoftware.GetService<PlcUnitProvider>();
        if (unitProvider is null)
        {
            yield break;
        }

        foreach (PlcUnit unit in unitProvider.UnitGroup.Units)
        {
            yield return new TypeOwner(unit.TypeGroup, unit.ExternalSourceGroup);
        }
    }

    private static void CollectMatches(
        TypeOwner owner,
        PlcTypeGroup group,
        string typeName,
        List<ResolvedTypeTarget> matches)
    {
        var type = group.Types.Find(typeName);
        if (type is not null)
        {
            matches.Add(new ResolvedTypeTarget(owner.ExternalSourceGroup, group, type, typeName));
        }

        foreach (PlcTypeUserGroup childGroup in group.Groups)
        {
            CollectMatches(owner, childGroup, typeName, matches);
        }
    }

    /// <summary>A software scope that owns a type tree: either the PLC itself or one software unit.</summary>
    private readonly struct TypeOwner
    {
        public TypeOwner(PlcTypeGroup rootTypeGroup, PlcExternalSourceSystemGroup externalSourceGroup)
        {
            RootTypeGroup = rootTypeGroup;
            ExternalSourceGroup = externalSourceGroup;
        }

        public PlcTypeGroup RootTypeGroup { get; }

        public PlcExternalSourceSystemGroup ExternalSourceGroup { get; }
    }
}

internal sealed class ResolvedTypeTarget
{
    public ResolvedTypeTarget(
        PlcExternalSourceSystemGroup externalSourceGroup,
        PlcTypeGroup group,
        PlcType? type,
        string documentName)
    {
        ExternalSourceGroup = externalSourceGroup;
        Group = group;
        Type = type;
        DocumentName = documentName;
    }

    /// <summary>
    /// The external source group of the software scope the type actually lives in — the PLC's for a
    /// PLC-scoped type, the unit's own for a unit-scoped one. Both GenerateSource (export) and
    /// CreateFromFile (import) must run against this one, or the round trip silently targets the
    /// wrong software context.
    /// </summary>
    public PlcExternalSourceSystemGroup ExternalSourceGroup { get; }

    public PlcTypeGroup Group { get; }

    public PlcType? Type { get; }

    public string DocumentName { get; }

    /// <summary>
    /// GenerateBlocksFromSource targets a PlcTypeUserGroup; the root PlcTypeSystemGroup is not one.
    /// Callers must keep the root case distinguishable so they can use a group-less overload when
    /// required.
    /// </summary>
    public PlcTypeUserGroup? UserGroup => Group as PlcTypeUserGroup;
}
