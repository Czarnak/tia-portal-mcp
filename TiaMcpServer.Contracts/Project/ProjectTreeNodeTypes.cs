namespace TiaMcpServer.Contracts;

public static class ProjectTreeNodeTypes
{
    public const string Device = "Device";
    public const string PlcSoftware = "PlcSoftware";
    public const string SoftwareUnit = "SoftwareUnit";
    public const string BlockFolder = "BlockFolder";
    public const string SystemBlockFolder = "SystemBlockFolder";
    public const string Ob = "OB";
    public const string Fb = "FB";
    public const string Fc = "FC";
    public const string GlobalDb = "GlobalDB";
    public const string InstanceDb = "InstanceDB";
    public const string ArrayDb = "ArrayDB";
    public const string Block = "Block";
    public const string TagTableFolder = "TagTableFolder";
    public const string TagTable = "TagTable";
    public const string TypeFolder = "TypeFolder";
    public const string Type = "Type";

    public static readonly IReadOnlyCollection<string> BlockLeaves = new HashSet<string>(StringComparer.Ordinal)
    {
        Ob, Fb, Fc, GlobalDb, InstanceDb, ArrayDb, Block
    };

    public static readonly IReadOnlyCollection<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Device, PlcSoftware, SoftwareUnit, BlockFolder, SystemBlockFolder,
        Ob, Fb, Fc, GlobalDb, InstanceDb, ArrayDb, Block,
        TagTableFolder, TagTable, TypeFolder, Type
    };

    public static void Validate(IReadOnlyList<ProjectTreeSelectorSegment>? selector)
    {
        if (selector is null)
        {
            return;
        }

        if (selector.Count == 0)
        {
            throw ProjectTreeSelectionException.Invalid("startSelector must be omitted or contain at least one segment.");
        }

        for (var index = 0; index < selector.Count; index++)
        {
            var segment = selector[index];
            if (segment is null || !Contains(All, segment.NodeType) || string.IsNullOrWhiteSpace(segment.Name))
            {
                throw ProjectTreeSelectionException.Invalid("startSelector contains an invalid segment.");
            }

            var parentType = index == 0 ? null : selector[index - 1].NodeType;
            if (!CanFollow(parentType, segment.NodeType))
            {
                throw ProjectTreeSelectionException.Invalid("startSelector contains an impossible parent-child transition.");
            }
        }
    }

    private static bool CanFollow(string? parentType, string childType)
    {
        if (parentType is null)
        {
            return string.Equals(childType, Device, StringComparison.Ordinal);
        }

        return parentType switch
        {
            Device => string.Equals(childType, PlcSoftware, StringComparison.Ordinal),
            PlcSoftware => IsOneOf(childType, SoftwareUnit, BlockFolder, TagTableFolder, TypeFolder),
            SoftwareUnit => IsOneOf(childType, BlockFolder, TagTableFolder, TypeFolder),
            BlockFolder => IsOneOf(childType, BlockFolder, SystemBlockFolder)
                || Contains(BlockLeaves, childType),
            SystemBlockFolder => string.Equals(childType, SystemBlockFolder, StringComparison.Ordinal)
                || Contains(BlockLeaves, childType),
            TagTableFolder => IsOneOf(childType, TagTableFolder, TagTable),
            TypeFolder => IsOneOf(childType, TypeFolder, Type),
            _ => false
        };
    }

    private static bool IsOneOf(string value, params string[] candidates)
        => Array.Exists(candidates, candidate => string.Equals(value, candidate, StringComparison.Ordinal));

    private static bool Contains(IReadOnlyCollection<string> values, string value)
    {
        foreach (var candidate in values)
        {
            if (string.Equals(candidate, value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
