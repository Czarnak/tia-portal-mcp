namespace TiaMcpServer.Contracts;

/// <summary>
/// Classifies every worker operation by its intent. Both the host and worker use this
/// shared classification to enforce access policy. An operation that has no explicit
/// classification is denied in read-only mode (deny-by-default).
/// </summary>
public enum OperationCapability
{
    /// <summary>Read-only observation: inspect project data without side effects.</summary>
    Observe,

    /// <summary>Creates temporary files internally (e.g. block export) but does not persist
    /// output. Allowed in read-only mode when cleanup is guaranteed.</summary>
    TemporaryExport,

    /// <summary>Side-effect-free internal safety-state read. Allowed in read-only mode but
    /// bound to the verified worker/Portal/project identity.</summary>
    SafetyRead,

    /// <summary>Invokes the Siemens compilation API. Not allowed in read-only mode because
    /// compilation may modify internal project state.</summary>
    Compile,

    /// <summary>Opens, creates, saves, archives, or closes a project.</summary>
    ProjectLifecycle,

    /// <summary>Modifies project data: blocks, tags, tag tables, user constants, network devices.</summary>
    ProjectMutation,

    /// <summary>Controls PLC runtime state: start, stop.</summary>
    OnlineControl
}
