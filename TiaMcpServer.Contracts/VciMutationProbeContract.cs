using System;
using System.Collections.Generic;
using System.IO;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Siemens-free vocabulary and semantic validation for the internal VCI Workspace Phase 1
/// mutation probe. It exposes no public MCP tool and no arbitrary member-selection escape hatch.
/// </summary>
public static class VciMutationProbeContract
{
    public const string OperationName = "probe_vci_mutation_contract";
    public const string SchemaVersion = "vci-mutation-probe/v1";

    public static IReadOnlyList<string> CaseIds { get; } = new[]
    {
        "P-INVENTORY",
        "M-CANARY",
        "M-GROUP",
        "M-WORKSPACE-ROOT",
        "M-WORKSPACE-LANGUAGE",
        "M-EXPORT",
        "M-DISCONNECT",
        "M-CONNECT",
        "M-P2W",
        "M-W2P",
        "M-DELETE-MAPPING",
        "M-DELETE-WORKSPACE",
        "M-DELETE-GROUP",
        "M-TX-GROUP",
        "M-TX-WORKSPACE",
        "M-TX-EXPORT",
        "M-TX-CONNECT",
        "M-TX-P2W",
        "M-TX-W2P",
        "M-TX-DISCONNECT",
        "M-TX-DELETE-WORKSPACE",
        "M-TX-DELETE-GROUP",
        "N-GROUP-NULL",
        "N-GROUP-EMPTY",
        "N-GROUP-WHITESPACE",
        "N-GROUP-DUPLICATE",
        "N-GROUP-INVALID",
        "N-WORKSPACE-NULL",
        "N-WORKSPACE-EMPTY",
        "N-WORKSPACE-WHITESPACE",
        "N-WORKSPACE-DUPLICATE",
        "N-WORKSPACE-INVALID",
        "N-WORKSPACE-PATH-RELATIVE",
        "N-WORKSPACE-PATH-MISSING-PARENT",
        "N-WORKSPACE-PATH-CONFLICT",
        "N-WORKSPACE-PATH-FILE",
        "N-WORKSPACE-LANGUAGE-NULL",
        "N-WORKSPACE-LANGUAGE-INVALID",
        "N-WORKSPACE-GLOBAL-LIBRARY-NULL",
        "N-WORKSPACE-GLOBAL-LIBRARY-INVALID",
        "N-OBJECT-NULL",
        "N-OBJECT-UNSUPPORTED",
        "N-OBJECT-FOREIGN",
        "N-OBJECT-DISPOSED",
        "N-OBJECT-ALREADY-MAPPED",
        "N-OBJECT-DELETED",
        "N-FORMAT-NULL",
        "N-FORMAT-EMPTY",
        "N-FORMAT-UNSUPPORTED",
        "N-FORMAT-WRONG-CASE",
        "N-FORMAT-MISMATCH",
        "N-FILENAME-INVALID",
        "N-FILENAME-ABSOLUTE",
        "N-FILENAME-TRAVERSAL",
        "N-FILENAME-COLLISION",
        "N-CONNECT-MISSING",
        "N-CONNECT-MALFORMED",
        "N-CONNECT-WRONG-OBJECT",
        "N-CONNECT-PARTIAL-FILE-SET",
        "N-SYNC-MISSING",
        "N-SYNC-MALFORMED",
        "N-SYNC-UNCHANGED",
        "N-SYNC-PROJECT-ONLY",
        "N-SYNC-WORKSPACE-ONLY",
        "N-SYNC-BOTH-SIDES",
        "N-SYNC-INVALID-ENUM",
        "N-DELETE-NONEMPTY",
        "N-DELETE-TWICE",
        "N-STALE-MAPPING-PROXY",
    };

    public static IReadOnlyList<string> Outcomes { get; } = new[]
    {
        "returned",
        "returned_null",
        "not_observable",
        "threw",
        "timed_out",
        "process_lost",
    };

    public static IReadOnlyList<string> NotObservableReasons { get; } = new[]
    {
        "signature_does_not_permit_argument",
        "selected_format_is_single_file",
        "selected_format_not_supported",
        "selected_engineering_object_not_found",
        "selected_workspace_not_found",
        "selected_mapping_not_found",
        "required_fixture_state_not_available",
        "baseline_and_changed_exports_identical",
        "expected_project_only_state_not_established",
        "transaction_not_supported",
        "harness_confinement_rejected_before_worker",
    };

    private static readonly HashSet<string> CaseIdSet = new HashSet<string>(CaseIds, StringComparer.Ordinal);

    private static readonly HashSet<string> ExportCases = new HashSet<string>(new[]
    {
        "P-INVENTORY", "M-EXPORT", "M-TX-EXPORT",
    }, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> FixedSynchronizationModes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["M-P2W"] = "ProjectToWorkspace",
            ["M-W2P"] = "WorkspaceToProject",
            ["M-TX-P2W"] = "ProjectToWorkspace",
            ["M-TX-W2P"] = "WorkspaceToProject",
        };

    public static bool IsKnownCase(string? caseId) => caseId is not null && CaseIdSet.Contains(caseId);

    public static string? Validate(VciMutationProbeRequestInfo request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (!string.Equals(request.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            return $"'schemaVersion' must be '{SchemaVersion}' (received '{request.SchemaVersion}').";
        }

        var identityError = ValidateRequiredIdentifier(request.RunId, "runId")
            ?? ValidateRequiredIdentifier(request.SessionId, "sessionId")
            ?? ValidateRequiredIdentifier(request.ScenarioId, "scenarioId")
            ?? ValidateRequiredIdentifier(request.CaseInstanceId, "caseInstanceId");
        if (identityError is not null)
        {
            return identityError;
        }

        if (!IsKnownCase(request.CaseId))
        {
            return $"'caseId' value '{request.CaseId}' is not a recognised mutation probe case.";
        }

        if (!string.Equals(request.Mode, "Inventory", StringComparison.Ordinal)
            && !string.Equals(request.Mode, "Apply", StringComparison.Ordinal))
        {
            return $"'mode' must be 'Inventory' or 'Apply' (received '{request.Mode}').";
        }

        if (string.Equals(request.CaseId, "P-INVENTORY", StringComparison.Ordinal))
        {
            if (!string.Equals(request.Mode, "Inventory", StringComparison.Ordinal))
            {
                return "'mode' must be 'Inventory' for case 'P-INVENTORY'.";
            }
        }
        else if (!string.Equals(request.Mode, "Apply", StringComparison.Ordinal))
        {
            return $"'mode' must be 'Apply' for mutation case '{request.CaseId}'.";
        }

        if (string.IsNullOrWhiteSpace(request.WorkspaceRoot) || !Path.IsPathRooted(request.WorkspaceRoot))
        {
            return "'workspaceRoot' must be an absolute path.";
        }

        var budgetError = ValidateBudget(request.MaxGroupDepth, "maxGroupDepth")
            ?? ValidateBudget(request.MaxGroups, "maxGroups")
            ?? ValidateBudget(request.MaxWorkspaces, "maxWorkspaces")
            ?? ValidateBudget(request.MaxMappings, "maxMappings")
            ?? ValidateBudget(request.MaxEngineeringObjects, "maxEngineeringObjects")
            ?? ValidateBudget(request.MaxCollectionItems, "maxCollectionItems");
        if (budgetError is not null)
        {
            return budgetError;
        }

        if (ExportCases.Contains(request.CaseId))
        {
            if (string.Equals(request.CaseId, "P-INVENTORY", StringComparison.Ordinal)
                && request.Workspace is null)
            {
                return "'workspace' is required for case 'P-INVENTORY'.";
            }

            if (request.EngineeringObject is null)
            {
                return $"'engineeringObject' is required for case '{request.CaseId}'.";
            }

            if (!string.Equals(request.FileFormat, "SimaticML", StringComparison.Ordinal))
            {
                return $"'fileFormat' must be exactly 'SimaticML' for case '{request.CaseId}'.";
            }
        }

        if (FixedSynchronizationModes.TryGetValue(request.CaseId, out var synchronizationMode)
            && !string.Equals(request.SynchronizationMode, synchronizationMode, StringComparison.Ordinal))
        {
            return $"'synchronizationMode' must be '{synchronizationMode}' for case '{request.CaseId}'.";
        }

        var isTransactionCase = request.CaseId.StartsWith("M-TX-", StringComparison.Ordinal);
        if (isTransactionCase && !request.RollbackTransaction)
        {
            return $"'rollbackTransaction' must be true for transaction case '{request.CaseId}'.";
        }

        if (!isTransactionCase && request.RollbackTransaction)
        {
            return $"'rollbackTransaction' must be false for non-transaction case '{request.CaseId}'.";
        }

        return null;
    }

    private static string? ValidateRequiredIdentifier(string? value, string field)
        => string.IsNullOrWhiteSpace(value) ? $"'{field}' must be a nonblank string." : null;

    private static string? ValidateBudget(int value, string field)
        => value < 1 ? $"'{field}' must be 1 or greater." : null;
}
