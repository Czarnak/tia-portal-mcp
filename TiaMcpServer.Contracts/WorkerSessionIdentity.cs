namespace TiaMcpServer.Contracts;

/// <summary>
/// Stable identity of the worker/TIA/project session that handled one request.
/// The tuple, not the project path alone, is the authority for guarded writes.
/// </summary>
public sealed class WorkerSessionIdentity
{
    /// <summary>Random id created once for the lifetime of one Openness worker process.</summary>
    public string WorkerSessionId { get; set; } = string.Empty;

    /// <summary>
    /// Monotonic generation incremented whenever the attached Portal or selected project handle
    /// changes, including close/reopen of the same project path.
    /// </summary>
    public long SessionGeneration { get; set; }

    /// <summary>Operating-system process id of the attached TIA Portal instance.</summary>
    public int? PortalProcessId { get; set; }

    /// <summary>Canonical project path, or null when the session currently has no project.</summary>
    public string? ProjectPath { get; set; }
}
