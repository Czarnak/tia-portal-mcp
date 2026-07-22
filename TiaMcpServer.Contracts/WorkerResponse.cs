using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public class WorkerResponse
{
    public bool Success { get; set; }

    public string? Payload { get; set; }

    public string? Error { get; set; }

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
}
