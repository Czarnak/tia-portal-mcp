namespace TiaMcpServer.Contracts.Worker;

/// <summary>
/// Exact host/worker wire contract. Identity enforcement is a protocol boundary, so a same-major
/// but stale worker is not compatible unless it advertises this version and capability set.
/// </summary>
public static class WorkerProtocol
{
    public const string Version = "project-tree-v3";

    public static readonly string[] RequiredCapabilities =
    {
        "expected-session-identity",
        "response-session-identity",
        "deterministic-project-selection",
        "typed-project-tree-selector",
        "typed-block-import-outcome-v1",
        "portal-process-listing-v1",
        "portal-project-selection-v1"
    };
}
