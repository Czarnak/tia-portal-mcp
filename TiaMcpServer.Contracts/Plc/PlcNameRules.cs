namespace TiaMcpServer.Contracts.Plc;

/// <summary>An object that occupies a name inside a PLC (<c>Kind</c> is one of the <see cref="PlcNameRules"/> kinds).</summary>
public sealed record PlcNamedObject(string Kind, string Name, string? Container);

/// <summary>
/// CPU-wide name rules: tags, user constants, blocks, system blocks, tag tables and block groups
/// share one case-insensitive namespace.
/// </summary>
public static class PlcNameRules
{
    public const string Tag = "Tag";
    public const string UserConstant = "UserConstant";
    public const string Block = "Block";
    public const string SystemBlock = "SystemBlock";
    public const string TagTable = "TagTable";
    public const string BlockGroup = "BlockGroup";

    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Returns the first occupant whose name equals <paramref name="name"/> case-insensitively,
    /// ignoring <paramref name="self"/> (the object being written or renamed).
    /// </summary>
    public static PlcNamedObject? FindCollision(
        string name,
        IEnumerable<PlcNamedObject> occupants,
        PlcNamedObject? self = null)
    {
        foreach (var occupant in occupants)
        {
            if (!Comparer.Equals(occupant.Name, name) || IsSelf(occupant, self))
            {
                continue;
            }

            return occupant;
        }

        return null;
    }

    private static bool IsSelf(PlcNamedObject occupant, PlcNamedObject? self)
        => self is not null
           && string.Equals(occupant.Kind, self.Kind, StringComparison.Ordinal)
           && Comparer.Equals(occupant.Name, self.Name)
           && Comparer.Equals(occupant.Container ?? string.Empty, self.Container ?? string.Empty);
}
