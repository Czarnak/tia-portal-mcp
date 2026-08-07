using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker;

internal sealed class NetworkAttributeProbeInfo
{
    public NetworkObjectSelectorInfo Target { get; set; } = new();

    public List<NetworkAttributeProbeEntryInfo> Attributes { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}

internal sealed class NetworkAttributeProbeEntryInfo
{
    public string Name { get; set; } = string.Empty;

    public string AccessMode { get; set; } = string.Empty;

    public List<string> SupportedClrTypeNames { get; set; } = new();

    public string? ObservedClrValueType { get; set; }

    public string? ExceptionCategory { get; set; }
}
