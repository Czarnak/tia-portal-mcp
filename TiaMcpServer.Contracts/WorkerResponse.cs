using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public class WorkerResponse
{
    /// <summary>Set by the pre-Siemens hello response.</summary>
    public string? ProtocolVersion { get; set; }

    /// <summary>Capabilities advertised by the pre-Siemens hello response.</summary>
    public List<string>? Capabilities { get; set; }

    public bool Success { get; set; }

    public string? Payload { get; set; }

    public string? Error { get; set; }

    /// <summary>
    /// Closed failure category from <see cref="WorkerFailureCategories"/>, set once at the point
    /// the failure is constructed and never mutated afterward — unlike <see cref="Warnings"/> and
    /// <see cref="ResolvedProjectPath"/>, which are patched in after the fact. Null on success.
    /// </summary>
    public string? FailureCategory { get; init; }

    /// <summary>
    /// Non-fatal degradation notes captured from the worker's Console.Error while THIS
    /// request was being handled (e.g. "Skipping device X: access denied"). Null when none.
    /// </summary>
    public List<string>? Warnings { get; set; }

    /// <summary>
    /// Absolute path of the project the worker actually operated on, or null when no project was
    /// attached. This is ground truth for session binding: the host binds to THIS, never to the
    /// path the caller requested, so a mistyped-but-real path cannot silently retarget a session.
    /// </summary>
    public string? ResolvedProjectPath { get; set; }

    /// <summary>
    /// Full identity of the worker/TIA/project session after this operation. A successful bound
    /// operation without this value is a broken postcondition; the host must never fall back to
    /// <see cref="ResolvedProjectPath"/> or caller input for guarded writes.
    /// </summary>
    public WorkerSessionIdentity? SessionIdentity { get; set; }
}
