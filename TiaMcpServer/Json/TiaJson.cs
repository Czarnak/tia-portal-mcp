using System.Text.Json;

namespace TiaMcpServer.Json;

/// <summary>
/// Shared System.Text.Json configuration for host-process output.
///
/// <para>
/// This covers host-side serialization for the legacy text contract: text rendered back to
/// the MCP client by tools not yet on the structured contract, the presentation audit JSONL
/// records, and stable hashing.
/// Structured tools render through CanonicalJson instead. The host↔worker wire format lives in
/// TiaMcpServer.Contracts.WorkerJson.
/// </para>
/// </summary>
public static class TiaJson
{
    /// <summary>
    /// Options for host-produced JSON. Compact on purpose: responses are token-budgeted and
    /// indentation is pure overhead. Keep this stable — audit records and their hashes
    /// are derived through it, so a formatting change alters every hash.
    /// </summary>
    public static readonly JsonSerializerOptions Presentation = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    static TiaJson()
    {
        // Frozen on purpose: audit records and their hashes are derived
        // through these options, so a formatting change would alter every hash.
        Presentation.MakeReadOnly(populateMissingResolver: true);
    }
}
