namespace TiaMcpServer.Contracts;

/// <summary>
/// Immutable access mode for the MCP server process. Resolved once at startup and cannot
/// be changed at runtime. Read-write permits in-project edits, compile and explicit project
/// lifecycle with confirmation for each lifecycle call; full additionally permits PLC runtime
/// control and runs lifecycle calls directly by policy.
/// </summary>
public enum McpAccessMode
{
    /// <summary>Observation and selection of already-open projects are allowed. This mode
    /// never opens, creates, saves or closes a project. Write tools are not exposed and
    /// prohibited operations are rejected before reaching the worker.</summary>
    ReadOnly,

    /// <summary>Observation, in-project edits, compile and project lifecycle with user confirmation;
    /// no PLC runtime control.</summary>
    ReadWrite,

    /// <summary>All classified capabilities, including PLC runtime control; lifecycle runs directly.</summary>
    Full
}
