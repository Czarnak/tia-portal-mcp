namespace TiaMcpServer.Worker;

/// <summary>
/// Structured outcome of one TIA Openness worker invocation. Replaces the "Error:"
/// string-prefix convention: success/failure is carried structurally and payload text
/// never drives classification. <see cref="Warnings"/> carries non-fatal degradation
/// notes captured from the worker's stderr.
/// </summary>
public sealed record WorkerCallResult(
    bool Success,
    string Payload,
    string? Error,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Project the worker actually operated on, when it reported one. Ground truth for session
    /// binding — see ProjectSessionBinding.
    /// </summary>
    public string? ResolvedProjectPath { get; init; }

    public static WorkerCallResult Ok(string payload, IReadOnlyList<string>? warnings = null)
        => new(true, payload, null, warnings ?? Array.Empty<string>());

    public static WorkerCallResult Fail(string error, IReadOnlyList<string>? warnings = null)
        => new(false, string.Empty, error, warnings ?? Array.Empty<string>());

    /// <summary>Agent-facing text for boundaries where an MCP tool returns a plain string.</summary>
    public string ToText()
        => Success ? Payload : $"Error: {Error}";
}
