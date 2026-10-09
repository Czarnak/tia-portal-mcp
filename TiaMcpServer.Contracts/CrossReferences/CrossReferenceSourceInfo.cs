namespace TiaMcpServer.Contracts.CrossReferences;

public class CrossReferenceSourceInfo
{
    public string Name { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Device { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public List<CrossReferenceTargetInfo> References { get; set; } = new List<CrossReferenceTargetInfo>();

    public List<CrossReferenceSourceInfo> Children { get; set; } = new List<CrossReferenceSourceInfo>();
}
