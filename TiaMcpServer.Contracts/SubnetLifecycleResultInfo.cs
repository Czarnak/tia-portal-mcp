namespace TiaMcpServer.Contracts;

public sealed class SubnetLifecycleResultInfo
{
    public string SubnetId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int NetworkDeviceCount { get; set; }
    public bool NetworkDeviceCountUnchanged { get; set; }
}
