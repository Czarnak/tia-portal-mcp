namespace TiaMcpServer.Contracts;

public class CrossReferenceTargetInfo
{
    public string Name { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Device { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public List<CrossReferenceLocationInfo> Locations { get; set; } = new List<CrossReferenceLocationInfo>();
}
