namespace TiaMcpServer.Contracts;

public class TagTableInfo
{
    public string Name { get; set; } = string.Empty;

    public string FolderPath { get; set; } = string.Empty;

    public bool IsDefault { get; set; } = false;

    public List<TagInfo> Tags { get; set; } = new List<TagInfo>();

    public List<UserConstantInfo> UserConstants { get; set; } = new List<UserConstantInfo>();
}
