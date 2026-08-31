using System;
using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Central classification of every worker operation by its capability. Both the host
/// (OperationAccessPolicy) and the worker (WorkerOperationAuthorization) consume this
/// single source of truth. An operation not listed here is denied in read-only mode
/// (deny-by-default).
/// </summary>
public static class OperationPolicyCatalog
{
    private static readonly IReadOnlyDictionary<string, OperationCapability> Classifications =
        BuildClassifications();

    /// <summary>
    /// Returns the capability for <paramref name="operation"/>, or null if the operation
    /// is not classified (unknown operations are denied in read-only mode).
    /// </summary>
    public static OperationCapability? GetCapability(string operation)
        => Classifications.TryGetValue(operation, out var cap) ? cap : null;

    /// <summary>
    /// True when <paramref name="operation"/> is allowed under the given access mode.
    /// Read-only mode allows only Observe and TemporaryExport.
    /// </summary>
    public static bool IsAllowed(McpAccessMode mode, string operation)
    {
        if (mode == McpAccessMode.ReadWrite)
        {
            return true;
        }

        var cap = GetCapability(operation);
        if (cap is null)
        {
            return false;
        }

        return cap.Value is OperationCapability.Observe or OperationCapability.TemporaryExport;
    }

    /// <summary>
    /// True when a worker request must carry the exact currently verified
    /// worker/Portal/project identity.
    /// </summary>
    public static bool RequiresExpectedSessionIdentity(string operation)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            return true;
        }

        if (string.Equals(operation, "open_project", StringComparison.Ordinal) ||
            string.Equals(operation, "create_project", StringComparison.Ordinal))
        {
            return false;
        }

        var capability = GetCapability(operation);
        return capability != OperationCapability.Observe &&
               capability != OperationCapability.TemporaryExport;
    }

    /// <summary>
    /// Every known operation name. Used by tests to verify completeness.
    /// </summary>
    public static IReadOnlyCollection<string> AllOperationNames => new List<string>(Classifications.Keys);

    private static IReadOnlyDictionary<string, OperationCapability> BuildClassifications()
    {
        var dict = new Dictionary<string, OperationCapability>(StringComparer.Ordinal)
        {
            // Observe (read-only safe)
            ["get_project_status"] = OperationCapability.Observe,
            ["browse_project_tree"] = OperationCapability.Observe,
            ["read_hardware_config"] = OperationCapability.Observe,
            ["read_hardware_page_candidates"] = OperationCapability.Observe,
            ["search_equipment_catalog"] = OperationCapability.Observe,
            ["read_cross_references"] = OperationCapability.Observe,
            ["list_tag_tables"] = OperationCapability.Observe,
            ["list_network_objects"] = OperationCapability.Observe,
            ["inspect_network_object"] = OperationCapability.Observe,
            ["probe_network_object_attributes"] = OperationCapability.Observe,

            // TemporaryExport (read-only safe, temporary files with cleanup)
            ["get_block_content"] = OperationCapability.TemporaryExport,

            // Compile (NOT read-only safe)
            ["compile_check"] = OperationCapability.Compile,

            // ProjectLifecycle (NOT read-only safe)
            ["open_project"] = OperationCapability.ProjectLifecycle,
            ["create_project"] = OperationCapability.ProjectLifecycle,
            ["save_project"] = OperationCapability.ProjectLifecycle,
            ["save_project_as"] = OperationCapability.ProjectLifecycle,
            ["archive_project"] = OperationCapability.ProjectLifecycle,
            ["close_project"] = OperationCapability.ProjectLifecycle,

            // ProjectMutation (NOT read-only safe)
            ["update_block_logic"] = OperationCapability.ProjectMutation,
            ["create_block"] = OperationCapability.ProjectMutation,
            ["delete_block"] = OperationCapability.ProjectMutation,
            ["create_block_group"] = OperationCapability.ProjectMutation,
            ["delete_block_group"] = OperationCapability.ProjectMutation,
            ["create_tag_table"] = OperationCapability.ProjectMutation,
            ["delete_tag_table"] = OperationCapability.ProjectMutation,
            ["create_tag"] = OperationCapability.ProjectMutation,
            ["update_tag"] = OperationCapability.ProjectMutation,
            ["delete_tag"] = OperationCapability.ProjectMutation,
            ["create_user_constant"] = OperationCapability.ProjectMutation,
            ["update_user_constant"] = OperationCapability.ProjectMutation,
            ["delete_user_constant"] = OperationCapability.ProjectMutation,
            ["add_network_device"] = OperationCapability.ProjectMutation,
            ["configure_network_device"] = OperationCapability.ProjectMutation,
            ["create_subnet"] = OperationCapability.ProjectMutation,
            ["update_subnet"] = OperationCapability.ProjectMutation,
            ["delete_subnet"] = OperationCapability.ProjectMutation,
            ["probe_subnet_lifecycle_mutations"] = OperationCapability.ProjectMutation,

            // OnlineControl (NOT read-only safe)
            ["start_plc"] = OperationCapability.OnlineControl,
            ["stop_plc"] = OperationCapability.OnlineControl,

            // Internal lifecycle probe (NOT read-only safe — may open a project)
            ["probe_project_status_for_lifecycle"] = OperationCapability.ProjectLifecycle,
        };

        return dict;
    }
}
