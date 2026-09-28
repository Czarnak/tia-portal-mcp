using System.Text.RegularExpressions;

namespace TiaMcpServer.Contracts;

public sealed class BlockAddress
{
    private static readonly Regex BlockSuffixPattern =
        new Regex(@"\s*\[[A-Za-z]+\d*\]$", RegexOptions.Compiled);

    private BlockAddress(
        string? plcName,
        string? unitName,
        IReadOnlyList<string> folderPath,
        string blockName,
        bool isDeterministic)
    {
        PlcName = plcName;
        UnitName = unitName;
        FolderPath = folderPath;
        BlockName = blockName;
        IsDeterministic = isDeterministic;
    }

    public string? PlcName { get; }

    public string? UnitName { get; }

    public IReadOnlyList<string> FolderPath { get; }

    public string BlockName { get; }

    public bool IsDeterministic { get; }

    public bool UsesSoftwareUnit => UnitName is not null;

    public static BlockAddress Parse(string blockPath)
    {
        if (string.IsNullOrWhiteSpace(blockPath))
        {
            throw new ArgumentException("Block path is required.", nameof(blockPath));
        }

        var segments = SplitSegments(blockPath);

        if (segments.Count == 1)
        {
            return new BlockAddress(
                plcName: null,
                unitName: null,
                folderPath: Array.Empty<string>(),
                blockName: StripBlockSuffix(segments[0]),
                isDeterministic: false);
        }

        if (segments.Count == 2 && !IsReservedSegment(segments[1]))
        {
            return new BlockAddress(
                plcName: segments[0],
                unitName: null,
                folderPath: Array.Empty<string>(),
                blockName: StripBlockSuffix(segments[1]),
                isDeterministic: false);
        }

        if (segments.Count >= 3 &&
            string.Equals(segments[1], "Blocks", StringComparison.OrdinalIgnoreCase))
        {
            return FromBlockSegments(segments[0], unitName: null, segments, startIndex: 2);
        }

        if (segments.Count >= 5 &&
            string.Equals(segments[1], "Units", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[3], "Blocks", StringComparison.OrdinalIgnoreCase))
        {
            return FromBlockSegments(segments[0], segments[2], segments, startIndex: 4);
        }

        throw new ArgumentException(
            "Block path must be 'BlockName', 'PLC/BlockName', 'PLC/Blocks/.../BlockName', or 'PLC/Units/Unit/Blocks/.../BlockName'.",
            nameof(blockPath));
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
            segments.Add("Units");
            segments.Add(UnitName);
        }

        if (IsDeterministic)
        {
            segments.Add("Blocks");
        }

        segments.AddRange(FolderPath);
        segments.Add(BlockName);

        return string.Join("/", segments);
    }

    private static BlockAddress FromBlockSegments(
        string plcName,
        string? unitName,
        IReadOnlyList<string> segments,
        int startIndex)
    {
        if (startIndex >= segments.Count)
        {
            throw new ArgumentException("Block path is missing a block name.", nameof(segments));
        }

        var folders = new List<string>();
        for (int i = startIndex; i < segments.Count - 1; i++)
        {
            folders.Add(segments[i]);
        }

        return new BlockAddress(
            plcName,
            unitName,
            folders.AsReadOnly(),
            StripBlockSuffix(segments[segments.Count - 1]),
            isDeterministic: true);
    }

    private static List<string> SplitSegments(string blockPath)
    {
        var result = new List<string>();
        foreach (var rawSegment in blockPath.Split('/'))
        {
            var segment = rawSegment.Trim();
            if (segment.Length == 0)
            {
                throw new ArgumentException("Block path cannot contain empty segments.", nameof(blockPath));
            }

            result.Add(segment);
        }

        return result;
    }

    private static string StripBlockSuffix(string blockName)
    {
        var stripped = BlockSuffixPattern.Replace(blockName, string.Empty).Trim();
        if (stripped.Length == 0)
        {
            throw new ArgumentException("Block path is missing a block name.", nameof(blockName));
        }

        return stripped;
    }

    private static bool IsReservedSegment(string segment)
    {
        return string.Equals(segment, "Blocks", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "Units", StringComparison.OrdinalIgnoreCase);
    }
}
