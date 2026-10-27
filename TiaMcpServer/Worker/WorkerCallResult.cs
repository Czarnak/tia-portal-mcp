using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Contracts.Vci;

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

    /// <summary>The Siemens exception behind a worker failure; absent for host-side failures.</summary>
    public EngineeringErrorInfo? EngineeringError { get; init; }

    public ConfigureNetworkDeviceResultInfo? ConfigureNetworkDeviceOutcome { get; init; }

    public HmiWriteOutcomeInfo? HmiWriteOutcome { get; init; }

    public VciWriteOutcomeInfo? VciWriteOutcome { get; init; }

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
}
