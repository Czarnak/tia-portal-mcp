namespace TiaMcpServer.Contracts;

public class UserConstantInfo
{
    public string Name { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    /// <summary>The constant's value; null when unreadable.</summary>
    public string? Value { get; set; } = string.Empty;
}
