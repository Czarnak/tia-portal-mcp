using System.Text.Json;

namespace TiaMcpServer.Json;

/// <summary>
/// Shared System.Text.Json configuration for host-process output.
///
/// <para>
/// This covers host-side serialization for the legacy text contract: text rendered back to
/// the MCP client by tools not yet on the structured contract, the presentation audit JSONL
/// records written by WriteSafetyService, the stable hashing that backs presentation-bound
/// safety tokens, and OperationBatchPayloadBudget's read-batch response length prediction.
/// Structured tools render through CanonicalJson instead. The host↔worker wire format is not
/// shared from here either: those options live with each process's transport
/// (TiaMcpServer/Worker/PersistentWorkerTransport.cs and the worker's Program.cs) and
/// currently differ — the worker omits nulls when writing. Consolidating them in
/// TiaMcpServer.Contracts, which already references System.Text.Json, is Phase 1 of
/// docs/roadmap/json-contract.md.
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

    static TiaJson()
    {
        // Frozen on purpose: audit records and the safety-token input hash are both derived
        // through these options, so a formatting change would invalidate outstanding tokens.
        Presentation.MakeReadOnly(populateMissingResolver: true);
    }
}
