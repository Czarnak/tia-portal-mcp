namespace TiaMcpServer.Contracts.Network;

/// <summary>Structural discovery scope and losses; optional scalar diagnostics remain separate.</summary>
public sealed class HardwareDiscoveryEvidenceInfo
{
    public string Scope { get; set; } = string.Empty;
    public bool Complete { get; set; }
    public List<HardwareDiscoveryFailureInfo> Failures { get; set; } = new();
}

/// <summary>One structural traversal or materialization failure recorded by the producing read.</summary>
public sealed class HardwareDiscoveryFailureInfo
{
    public string Stage { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
