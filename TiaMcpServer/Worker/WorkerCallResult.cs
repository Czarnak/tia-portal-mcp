using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;

namespace TiaMcpServer.Worker;

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
    /// writes, which render through <c>WriteSafetyTooling.BuildApplyResult</c>). Always emits
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
