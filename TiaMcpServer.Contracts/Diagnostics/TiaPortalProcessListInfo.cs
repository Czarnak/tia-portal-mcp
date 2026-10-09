namespace TiaMcpServer.Contracts.Diagnostics;

public sealed class TiaPortalProcessListInfo
{
    public int? AttachedProcessId { get; set; }

    public List<TiaPortalProcessInfo> Processes { get; set; } = new();
}
