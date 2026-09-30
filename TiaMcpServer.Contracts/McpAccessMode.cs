namespace TiaMcpServer.Contracts;

/// <summary>
/// Immutable access mode for the MCP server process. Resolved once at startup and cannot
/// be changed at runtime. Read-write permits in-project edits and compile; full additionally
/// permits explicit project lifecycle and PLC runtime control.
/// </summary>
public enum McpAccessMode
{
    /// <summary>Only observation operations are allowed. Write tools are not exposed and
    /// prohibited operations are rejected before reaching the worker.</summary>
    ReadOnly,

    /// <summary>Observation, in-project edits and compile; no persistence or runtime control.</summary>
    ReadWrite,

    /// <summary>All classified capabilities, including project lifecycle and PLC runtime control.</summary>
    Full
}
