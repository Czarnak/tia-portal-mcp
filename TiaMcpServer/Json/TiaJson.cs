using System.Text.Json;

namespace TiaMcpServer.Json;

/// <summary>
/// Shared System.Text.Json configuration for host-process output.
///
/// <para>
/// This covers host-side serialization: text rendered back to the MCP client, the audit
/// JSONL records written by WriteSafetyService, and the stable hashing that backs safety
/// tokens. The host↔worker wire
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
    /// Options for host-produced JSON. Compact on purpose: responses are token-budgeted and
    /// indentation is pure overhead. Keep this stable — audit records and the safety-token
    /// input hash are both derived through it, so a formatting change invalidates tokens.
    /// </summary>
    public static readonly JsonSerializerOptions Presentation = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
