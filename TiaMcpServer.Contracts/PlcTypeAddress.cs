using System;
using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// A path to a PLC data type (UDT), parsed into the parts needed to walk a live project.
///
/// <para>
/// Deliberately mirrors <see cref="BlockAddress"/>: same field shape, same deterministic /
/// non-deterministic distinction, same trimming rules. It accepts exactly the paths
/// ProjectTreeWalker prints for Type nodes, so a path copied out of browse_project_tree works
/// without editing.
/// </para>
/// </summary>
public sealed class PlcTypeAddress
{
    private const string TypesSegment = "Types";
    private const string UnitsSegment = "Units";

    private PlcTypeAddress(
        string? plcName,
        string? unitName,
        IReadOnlyList<string> folderPath,
        string typeName,
        bool isDeterministic)
    {
        PlcName = plcName;
        UnitName = unitName;
        FolderPath = folderPath;
        TypeName = typeName;
        IsDeterministic = isDeterministic;
    }

    public string? PlcName { get; }

    public string? UnitName { get; }

    public IReadOnlyList<string> FolderPath { get; }

    public string TypeName { get; }

    public bool IsDeterministic { get; }

    public bool UsesSoftwareUnit => UnitName is not null;

    public static PlcTypeAddress Parse(string typePath)
    {
        if (string.IsNullOrWhiteSpace(typePath))
        {
            throw new ArgumentException("Type path is required.", nameof(typePath));
        }

        var segments = SplitSegments(typePath);

        if (segments.Count == 1)
        {
            return new PlcTypeAddress(
                plcName: null,
                unitName: null,
                folderPath: Array.Empty<string>(),
                typeName: segments[0],
                isDeterministic: false);
        }

        if (segments.Count == 2 && !IsReservedSegment(segments[1]))
        {
            return new PlcTypeAddress(
                plcName: segments[0],
                unitName: null,
                folderPath: Array.Empty<string>(),
                typeName: segments[1],
                isDeterministic: false);
        }

        if (segments.Count >= 3 && IsSegment(segments[1], TypesSegment))
        {
            return FromTypeSegments(segments[0], unitName: null, segments, startIndex: 2);
        }

        if (segments.Count >= 5 &&
            IsSegment(segments[1], UnitsSegment) &&
            IsSegment(segments[3], TypesSegment))
        {
            return FromTypeSegments(segments[0], segments[2], segments, startIndex: 4);
        }

        throw new ArgumentException(
            "Type path must be 'TypeName', 'PLC/TypeName', 'PLC/Types/.../TypeName', or "
            + "'PLC/Units/Unit/Types/.../TypeName'.",
            nameof(typePath));
    }

    public string ToDisplayPath()
    {
        var segments = new List<string>();

        if (PlcName is not null)
        {
            segments.Add(PlcName);
        }

        if (UnitName is not null)
        {
            segments.Add(UnitsSegment);
            segments.Add(UnitName);
        }

        if (IsDeterministic)
        {
            segments.Add(TypesSegment);
        }

        segments.AddRange(FolderPath);
        segments.Add(TypeName);

        return string.Join("/", segments);
    }

    private static PlcTypeAddress FromTypeSegments(
        string plcName,
        string? unitName,
        IReadOnlyList<string> segments,
        int startIndex)
    {
        if (startIndex >= segments.Count)
        {
            throw new ArgumentException("Type path is missing a type name.", nameof(segments));
        }

        var folders = new List<string>();
        for (int i = startIndex; i < segments.Count - 1; i++)
        {
            folders.Add(segments[i]);
        }

        return new PlcTypeAddress(
            plcName,
            unitName,
            folders.AsReadOnly(),
            segments[segments.Count - 1],
            isDeterministic: true);
    }

    private static List<string> SplitSegments(string typePath)
    {
        var result = new List<string>();
        foreach (var rawSegment in typePath.Split('/'))
        {
            var segment = rawSegment.Trim();
            if (segment.Length == 0)
            {
                throw new ArgumentException("Type path cannot contain empty segments.", nameof(typePath));
            }

            result.Add(segment);
        }

        return result;
    }

    private static bool IsSegment(string segment, string expected)
        => string.Equals(segment, expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsReservedSegment(string segment)
        => IsSegment(segment, TypesSegment) || IsSegment(segment, UnitsSegment);
}