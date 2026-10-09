namespace TiaMcpServer.Contracts.Diagnostics;

public sealed class TiaPortalProcessInfo
{
    public int ProcessId { get; set; }

    public string? ProjectPath { get; set; }

    public bool HasUserInterface { get; set; }

    public bool AttachedByThisWorker { get; set; }
}
