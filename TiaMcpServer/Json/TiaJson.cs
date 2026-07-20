using System.Text.Json;

namespace TiaMcpServer.Json;

/// <summary>
/// Shared System.Text.Json configuration for host-process output.
///
/// <para>
/// This covers only text the host renders back to the MCP client. The host↔worker wire
/// format is deliberately NOT shared from here: those options live with each process's
/// transport (TiaMcpServer/Worker/PersistentWorkerTransport.cs and the worker's Program.cs),
/// they differ on purpose — the worker omits nulls when writing — and unifying them would
/// require a System.Text.Json package reference on the dependency-free
/// TiaMcpServer.Contracts assembly.
/// </para>
/// </summary>
public static class TiaJson
{
    /// <summary>
    /// Options for JSON returned to the MCP client. Compact on purpose: responses are
    /// token-budgeted and indentation is pure overhead.
    /// </summary>
    public static readonly JsonSerializerOptions Presentation = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
