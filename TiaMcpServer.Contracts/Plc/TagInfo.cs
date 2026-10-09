namespace TiaMcpServer.Contracts.Plc;

public class TagInfo
{
    public string Name { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public string LogicalAddress { get; set; } = string.Empty;

    /// <summary>External-access flags; null when unreadable or not supported (always null from <c>ReadAll</c>).</summary>
    public bool? ExternalAccessible { get; set; }

    public bool? ExternalVisible { get; set; }

    public bool? ExternalWritable { get; set; }
}
