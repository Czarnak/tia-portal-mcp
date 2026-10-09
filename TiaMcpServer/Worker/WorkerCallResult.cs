using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Worker;

internal enum WorkerDispatchState
{
    NotSent,
    Sent,
    Unknown
}

/// <summary>
/// Structured outcome of one TIA Openness worker invocation. Replaces the "Error:"
/// string-prefix convention: success/failure is carried structurally and payload text
/// never drives classification. <see cref="Warnings"/> carries non-fatal degradation
/// notes captured from the worker's stderr. <see cref="FailureCategory"/> is the closed
/// vocabulary from <see cref="WorkerFailureCategories"/> — null on success, always one of
/// its approved values on failure.
/// </summary>
public sealed record WorkerCallResult(
    bool Success,
    string Payload,
    string? Error,
    string? FailureCategory,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Project the worker actually operated on, when it reported one. Ground truth for session
    /// binding — see ProjectSessionBinding.
    /// </summary>
    public string? ResolvedProjectPath { get; init; }

    /// <summary>Complete worker/Portal/project identity observed for this response.</summary>
    public WorkerSessionIdentity? SessionIdentity { get; init; }

    /// <summary>Actual Portal attachment reported by the worker, including without a selected project.</summary>
    public int? PortalProcessId { get; init; }

    /// <summary>Typed update_block_logic evidence; absent for unrelated calls.</summary>
    public BlockImportOutcomeInfo? BlockImportOutcome { get; init; }

    /// <summary>Host-observed request dispatch provenance; never serialized to callers.</summary>
    internal WorkerDispatchState DispatchState { get; init; } = WorkerDispatchState.Unknown;

    /// <summary>The worker returned success before host identity validation rejected its response.</summary>
    internal bool IsPostOperationFailure { get; init; }

    public static WorkerCallResult Ok(string payload, IReadOnlyList<string>? warnings = null)
        => new(true, payload, null, null, warnings ?? Array.Empty<string>());

    /// <summary>
    /// Builds a failure result. <paramref name="failureCategory"/> must be one of
    /// <see cref="WorkerFailureCategories"/>'s approved values — this is validated here so an
    /// unknown category can never reach a caller.
    /// </summary>
    public static WorkerCallResult Fail(
        string failureCategory,
        string error,
        IReadOnlyList<string>? warnings = null)
    {
        if (!WorkerFailureCategories.IsKnown(failureCategory))
        {
            throw new ArgumentException(
                $"'{failureCategory}' is not an approved WorkerFailureCategories value.",
                nameof(failureCategory));
        }

        return new(false, string.Empty, error, failureCategory, warnings ?? Array.Empty<string>());
    }

    /// <summary>Agent-facing text for boundaries where an MCP tool returns a plain string.</summary>
    public string ToText()
        => Success ? Payload : $"Error: {Error}";

    /// <summary>
    /// Structured agent-facing envelope for direct lifecycle results (as opposed to guarded
    /// writes, which render through the guarded write pipeline). Always emits
    /// <c>success</c>, <c>payload</c>, <c>failureCategory</c>, <c>error</c>, and <c>warnings</c> so
    /// the category is a first-class, independently readable field rather than text embedded in
    /// a message string.
    /// </summary>
    public string ToEnvelopeText()
        => JsonSerializer.Serialize(
            new
            {
                success = Success,
                payload = Payload,
                failureCategory = FailureCategory,
                error = Error,
                warnings = Warnings,
                sessionIdentity = SessionIdentity
            },
            TiaJson.Presentation);
}
