namespace TiaMcpServer.Contracts.Safety;

/// <summary>
/// Classifies every worker operation by its intent. Both the host and worker use this
/// shared classification to enforce access policy. An operation that has no explicit
/// classification is denied in every mode (deny-by-default).
/// </summary>
public enum OperationCapability
{
    /// <summary>Read-only observation: inspect project data without side effects.</summary>
    Observe,

    /// <summary>Creates temporary files internally (e.g. block export) but does not persist
    /// output. Allowed in read-only mode when cleanup is guaranteed.</summary>
    TemporaryExport,

    /// <summary>Invokes the Siemens compilation API. Not allowed in read-only mode because
    /// compilation may modify internal project state.</summary>
    Compile,

    /// <summary>Opens, creates, saves, archives, or closes a project.</summary>
    ProjectLifecycle,

    /// <summary>Modifies project data: blocks, tags, tag tables, user constants, network devices.</summary>
    ProjectMutation,

    /// <summary>Reserved for PLC runtime control. No operation is classified here.</summary>
    OnlineControl,

    /// <summary>Selects an already-open Portal project without opening, saving, or closing it.</summary>
    SessionSelection
}
