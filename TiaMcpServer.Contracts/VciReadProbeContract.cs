using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Siemens-free closed vocabulary and semantic validation for the internal
/// <c>probe_vci_read_contract</c> worker operation (VCI Workspace Phase 1 read-only probe).
///
/// <para>
/// <see cref="CaseIds"/> is the locked read-only case vocabulary from the Phase 1 plan — exactly
/// 20 case IDs, no extras. <see cref="Outcomes"/> is the closed set of terminal outcome strings a
/// probe case result may report. Both are shared by the (not-yet-implemented) worker service and
/// the PowerShell live-evidence harness so the vocabulary is defined in exactly one place.
/// </para>
///
/// <para>
/// This type never calls into Siemens Openness. It only knows about the shape and semantics of
/// the request; resolving a case against a live TIA Portal project is a later task.
/// </para>
/// </summary>
public static class VciReadProbeContract
{
    /// <summary>The only worker operation this contract describes.</summary>
    public const string OperationName = "probe_vci_read_contract";

    /// <summary>Wire schema version stamped on every request and result envelope.</summary>
    public const string SchemaVersion = "vci-read-probe/v1";

    /// <summary>
    /// The locked read-only case vocabulary. Exactly 20 entries — no extras, no fewer — matching
    /// the "Locked Read-Only Case Vocabulary" table in the Phase 1 plan.
    /// </summary>
    public static IReadOnlyList<string> CaseIds { get; } = new[]
    {
        "N-FMT-FOREIGN",
        "N-FMT-NULL",
        "N-FMT-UNSUPPORTED",
        "N-GRP-FIND-EMPTY",
        "N-GRP-FIND-MISSING",
        "N-GRP-FIND-NULL",
        "N-GRP-FIND-WHITESPACE",
        "N-MAP-INACCESSIBLE-FILE",
        "N-MAP-MISSING-FILE",
        "N-WS-FIND-EMPTY",
        "N-WS-FIND-MISSING",
        "N-WS-FIND-NULL",
        "N-WS-FIND-WHITESPACE",
        "R-CANARY",
        "R-FMT",
        "R-GRP",
        "R-MAP",
        "R-REP",
        "R-SVC",
        "R-WS",
    };

    /// <summary>
    /// The closed set of terminal outcomes a probe case result may report. <c>timed_out</c> and
    /// <c>process_lost</c> are evidence-layer outcomes synthesized only by the live harness — worker
    /// code never emits them.
    /// </summary>
    public static IReadOnlyList<string> Outcomes { get; } = new[]
    {
        "returned",
        "returned_null",
        "not_observable",
        "threw",
        "timed_out",
        "process_lost",
    };

    /// <summary>Case IDs whose defining invocation passes an explicit <c>null</c> argument and must
    /// remain constructible without any selector being supplied.</summary>
    private static readonly HashSet<string> ExplicitNullArgumentCases = new HashSet<string>(
        new[] { "N-GRP-FIND-NULL", "N-WS-FIND-NULL", "N-FMT-NULL" },
        StringComparer.Ordinal);

    private static readonly HashSet<string> CaseIdSet =
        new HashSet<string>(CaseIds, StringComparer.Ordinal);

    /// <summary>True when <paramref name="caseId"/> is exactly one of the locked <see cref="CaseIds"/>.</summary>
    public static bool IsKnownCase(string? caseId) => caseId is not null && CaseIdSet.Contains(caseId);

    /// <summary>
    /// Validates the semantic rules a <see cref="VciProbeRequestInfo"/> must satisfy regardless of
    /// which case it targets: nonblank identifiers, a known case ID, positive budgets, and (for
    /// <c>R-FMT</c> only) both a workspace and an engineering-object selector.
    ///
    /// <para>Returns <see langword="null"/> when the request is valid, or a deterministic
    /// human-readable rejection message naming the offending field otherwise.</para>
    /// </summary>
    public static string? Validate(VciProbeRequestInfo request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (!string.Equals(request.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            return $"'schemaVersion' must be '{SchemaVersion}' (received '{request.SchemaVersion}').";
        }

        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            return "'runId' must be a nonblank string.";
        }

        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            return "'sessionId' must be a nonblank string.";
        }

        if (string.IsNullOrWhiteSpace(request.CaseInstanceId))
        {
            return "'caseInstanceId' must be a nonblank string.";
        }

        if (!IsKnownCase(request.CaseId))
        {
            return $"'caseId' value '{request.CaseId}' is not a recognised probe case.";
        }

        if (request.MaxGroupDepth < 1)
        {
            return "'maxGroupDepth' must be 1 or greater.";
        }

        if (request.MaxGroups < 1)
        {
            return "'maxGroups' must be 1 or greater.";
        }

        if (request.MaxWorkspaces < 1)
        {
            return "'maxWorkspaces' must be 1 or greater.";
        }

        if (request.MaxMappings < 1)
        {
            return "'maxMappings' must be 1 or greater.";
        }

        if (request.MaxEngineeringObjects < 1)
        {
            return "'maxEngineeringObjects' must be 1 or greater.";
        }

        if (request.MaxCollectionItems < 1)
        {
            return "'maxCollectionItems' must be 1 or greater.";
        }

        // Explicit-null-argument cases (N-GRP-FIND-NULL, N-WS-FIND-NULL, N-FMT-NULL) never require
        // a selector: the case itself supplies a literal null to the Siemens call being probed.
        if (ExplicitNullArgumentCases.Contains(request.CaseId))
        {
            return null;
        }

        if (string.Equals(request.CaseId, "R-FMT", StringComparison.Ordinal))
        {
            if (request.Workspace is null)
            {
                return "'workspace' is required for case 'R-FMT'.";
            }

            if (request.EngineeringObject is null)
            {
                return "'engineeringObject' is required for case 'R-FMT'.";
            }
        }

        return null;
    }
}
