namespace TiaMcpServer.Contracts;

public sealed class NetworkVerificationCheckInfo
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Expected { get; set; }
    public string? Observed { get; set; }
    public string? Message { get; set; }
}

public sealed class NetworkMutationVerificationInfo
{
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, string> Identity { get; set; } = new Dictionary<string, string>();
    public List<NetworkVerificationCheckInfo> Checks { get; set; } = new List<NetworkVerificationCheckInfo>();
    public string? Message { get; set; }
}
