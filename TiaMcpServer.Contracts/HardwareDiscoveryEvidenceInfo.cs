namespace TiaMcpServer.Contracts;

public sealed class HardwareDiscoveryEvidenceInfo
{
    public string Scope { get; set; } = string.Empty;
    public bool Complete { get; set; }
    public List<HardwareDiscoveryFailureInfo> Failures { get; set; } = new();
}

public sealed class HardwareDiscoveryFailureInfo
{
    public string Stage { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
