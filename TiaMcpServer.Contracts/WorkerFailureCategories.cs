using System;
using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Closed vocabulary of failure categories shared by the worker, the wire contract
/// (<see cref="WorkerResponse.FailureCategory"/>), and the host result (the host's
/// <c>WorkerCallResult.FailureCategory</c> references these same constants directly, since
/// <c>TiaMcpServer</c> already depends on this assembly). Every failure must use one of these
/// values; nothing downstream may invent or infer a category from free text.
/// </summary>
public static class WorkerFailureCategories
{
    /// <summary>The caller supplied missing, malformed, or otherwise invalid input.</summary>
    public const string ValidationError = "validation_error";

    /// <summary>The requested project conflicts with what this session/worker is already bound to or has open.</summary>
    public const string BindingConflict = "binding_conflict";

    /// <summary>The current project state no longer matches what a safety token was issued against.</summary>
    public const string StateChanged = "state_changed";

    /// <summary>The requested target could not be resolved from the supplied selector.</summary>
    public const string TargetNotFound = "target_not_found";

    /// <summary>The supplied selector resolved to more than one target.</summary>
    public const string TargetAmbiguous = "target_ambiguous";

    /// <summary>The supplied target evidence no longer matches the resolved target.</summary>
    public const string TargetEvidenceMismatch = "target_evidence_mismatch";

    /// <summary>The selected target kind is not supported by the requested operation.</summary>
    public const string TargetKindUnsupported = "target_kind_unsupported";

    /// <summary>The worker responded, but the operation itself failed for a reason not covered by another category.</summary>
    public const string WorkerOperationFailed = "worker_operation_failed";

    /// <summary>The worker did not respond within the request timeout. The outcome is unknown; never retried automatically.</summary>
    public const string WorkerTimeout = "worker_timeout";

    /// <summary>The worker process crashed, the pipe broke, or the response was null/malformed. The outcome is unknown; never retried automatically.</summary>
    public const string WorkerCrashed = "worker_crashed";

    /// <summary>An operation reported success but a required postcondition (e.g. a resolved project path) was missing.</summary>
    public const string PostconditionFailed = "postcondition_failed";

    /// <summary>The operation is not permitted in the current access mode (e.g. a write attempted in read-only mode).</summary>
    public const string AccessDenied = "access_denied";

    /// <summary>
    /// The worker reported success but its payload did not match the declared result contract for
    /// the operation — malformed, unknown, incorrectly cased, incorrectly typed, or structurally
    /// invalid. The operation may well have been performed; only the response is untrustworthy.
    /// </summary>
    public const string ProtocolError = "protocol_error";

    /// <summary>The supplied cursor was missing, malformed, or could not be decoded.</summary>
    public const string InvalidCursor = "invalid_cursor";

    /// <summary>The cursor was issued for a different set of list filters.</summary>
    public const string CursorFilterMismatch = "cursor_filter_mismatch";

    /// <summary>The cursor was issued against a different project snapshot.</summary>
    public const string CursorSnapshotMismatch = "cursor_snapshot_mismatch";

    /// <summary>The cursor points beyond the available result range.</summary>
    public const string CursorOutOfRange = "cursor_out_of_range";

    /// <summary>The cursor was issued for a different verified worker/project binding.</summary>
    public const string CursorBindingMismatch = "cursor_binding_mismatch";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        ValidationError,
        BindingConflict,
        StateChanged,
        TargetNotFound,
        TargetAmbiguous,
        TargetEvidenceMismatch,
        TargetKindUnsupported,
        WorkerOperationFailed,
        WorkerTimeout,
        WorkerCrashed,
        PostconditionFailed,
        AccessDenied,
        ProtocolError,
        InvalidCursor,
        CursorFilterMismatch,
        CursorSnapshotMismatch,
        CursorOutOfRange,
        CursorBindingMismatch
    };

    /// <summary>True when <paramref name="value"/> is exactly one of the approved category constants.</summary>
    public static bool IsKnown(string? value) => value is not null && Known.Contains(value);
}
