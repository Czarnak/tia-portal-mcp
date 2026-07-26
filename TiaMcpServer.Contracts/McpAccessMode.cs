namespace TiaMcpServer.Contracts;

/// <summary>
/// Immutable access mode for the MCP server process. Resolved once at startup and cannot
/// be changed at runtime. Read-only mode blocks all mutation operations; read-write mode
/// preserves the full tool surface with the existing preview-and-apply safety model.
/// </summary>
public enum McpAccessMode
{
    /// <summary>Only observation operations are allowed. Write tools are not exposed and
    /// prohibited operations are rejected before reaching the worker.</summary>
    ReadOnly,

    /// <summary>Full tool surface. Writes follow the existing preview-and-apply safety flow.</summary>
    ReadWrite
}
