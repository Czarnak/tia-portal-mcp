namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// PLC object kinds and the case-insensitive name comparer. Name collisions are not checked
/// here: TIA Portal rejects them itself.
/// </summary>
public static class PlcNameRules
{
    public const string Tag = "Tag";
    public const string UserConstant = "UserConstant";
    public const string Block = "Block";
    public const string TagTable = "TagTable";
    public const string BlockGroup = "BlockGroup";

    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;
}
