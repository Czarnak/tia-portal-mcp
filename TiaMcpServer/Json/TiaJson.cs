using System.Text.Json;

namespace TiaMcpServer.Json;

/// <summary>
/// Compact camelCase System.Text.Json options for host-side size checks.
///
/// <para>
/// Production uses it only to measure a <c>BlockImportOutcome</c> the same way the worker
/// projects it (<c>BlockImportOutcomeValidator</c>). Tool responses
/// render through CanonicalJson, and the host↔worker wire format lives in
/// TiaMcpServer.Contracts.Json.WorkerJson.
/// </para>
/// </summary>
public static class TiaJson
{
    /// <summary>Compact on purpose; frozen so a size check cannot be changed by a caller.</summary>
    public static readonly JsonSerializerOptions Presentation = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    static TiaJson()
    {
        Presentation.MakeReadOnly(populateMissingResolver: true);
    }
}
