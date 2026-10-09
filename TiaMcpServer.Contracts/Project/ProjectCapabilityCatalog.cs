namespace TiaMcpServer.Contracts.Project;

/// <summary>
/// Project-scoped public tools and batch items. Internal worker probes are represented by their
/// owning public operation; independent Portal/server inventory and catalog searches are omitted.
/// This table describes delivery for a container kind, not access-mode authorization.
/// </summary>
public static class ProjectCapabilityCatalog
{
    private static readonly string[] BasicLocalOperations =
    {
        "bind_project", "open_project", "get_project_status"
    };

    private static readonly string[] StandaloneLifecycleOperations =
    {
        "create_project", "save_project_as", "archive_project", "close_project"
    };

    /// <summary>Reads that run against the common <c>ProjectBase</c> root of any open container.</summary>
    private static readonly string[] ProjectContentReadOperations =
    {
        "browse_project_tree", "plc_read", "network_read", "read_cross_references", "hmi_read",
        "get_block_content", "get_type_content", "list_tag_tables",
        "read_hardware_config", "list_network_objects", "inspect_network_object"
    };

    /// <summary>
    /// Content writes, compile and save against <c>ProjectBase</c>; a local session saves through
    /// <c>LocalSession.Save()</c> and never checks in to the Project Server.
    /// </summary>
    private static readonly string[] ProjectContentOperations =
    {
        "plc_write", "network_write", "compile_check", "save_project",
        "update_block_logic", "update_type_content", "create_tag_table", "delete_tag_table",
        "create_tag", "update_tag", "delete_tag", "create_user_constant",
        "update_user_constant", "delete_user_constant", "create_block", "delete_block",
        "create_block_group", "delete_block_group",
        "add_network_device", "configure_network_device", "create_subnet", "update_subnet",
        "delete_subnet"
    };

    /// <summary>Internal qualification probes that save or compile-and-revert; never delivered for local sessions.</summary>
    private static readonly string[] StandaloneOnlyProbes =
    {
        "probe_io_system_qualification", "probe_subnet_lifecycle_mutations"
    };

    private static readonly string[] NonProjectMethods =
    {
        "hello", "select_portal_project", "list_tia_portal_processes", "list_server_connections",
        "list_server_groups", "list_server_projects", "list_local_sessions", "get_lock_state",
        "search_equipment_catalog"
    };

    private static readonly IReadOnlyList<ProjectCapabilityInfo> Standalone = Build(ProjectContainerKinds.StandaloneProject);
    private static readonly IReadOnlyList<ProjectCapabilityInfo> Local = Build(ProjectContainerKinds.LocalSession);
    private static readonly IReadOnlyList<ProjectCapabilityInfo> Server = Build(ProjectContainerKinds.ServerProject);

    public static IReadOnlyList<ProjectCapabilityInfo> Describe(string containerKind)
    {
        var source = containerKind switch
        {
            ProjectContainerKinds.StandaloneProject => Standalone,
            ProjectContainerKinds.LocalSession => Local,
            ProjectContainerKinds.ServerProject => Server,
            _ => throw new ArgumentOutOfRangeException(nameof(containerKind), containerKind, "Unknown project container kind.")
        };

        // DTOs are mutable for JSON; never expose shared catalog instances to a caller.
        return source.Select(item => new ProjectCapabilityInfo
        {
            Operation = item.Operation,
            Applicability = item.Applicability
        }).ToArray();
    }

    /// <summary>
    /// Maps a worker method or public operation to the catalog operation that gates it, or null
    /// when the method is not project-scoped (Portal/server inventory, catalog search).
    /// </summary>
    public static string? OperationFor(string method)
    {
        if (NonProjectMethods.Contains(method, StringComparer.Ordinal)) return null;
        if (method.StartsWith("hmi_", StringComparison.Ordinal) && method != "hmi_read") return "hmi_read";
        return method switch
        {
            "browse_project_tree_v3_snapshot" or "read_hardware_page_candidates" => "browse_project_tree",
            "get_basic_project_status" => "get_project_status",
            "probe_open_project_rebind" or "probe_project_status_for_lifecycle" => "open_project",
            "probe_network_object_attributes" => "network_read",
            "probe_io_system_qualification" or "probe_subnet_lifecycle_mutations" => "network_write",
            _ => method
        };
    }

    public static bool Supports(string containerKind, string operation)
    {
        if (string.IsNullOrWhiteSpace(operation) || OperationFor(operation) is not { } mapped) return false;
        if (containerKind == ProjectContainerKinds.StandaloneProject)
            return Standalone.Any(item => item.Operation == mapped);
        if (containerKind == ProjectContainerKinds.LocalSession)
            return !StandaloneOnlyProbes.Contains(operation, StringComparer.Ordinal)
                && (BasicLocalOperations.Contains(mapped, StringComparer.Ordinal)
                || ProjectContentReadOperations.Contains(mapped, StringComparer.Ordinal)
                || ProjectContentOperations.Contains(mapped, StringComparer.Ordinal));
        return false;
    }

    private static IReadOnlyList<ProjectCapabilityInfo> Build(string containerKind)
    {
        var result = new List<ProjectCapabilityInfo>();
        foreach (var operation in BasicLocalOperations)
            result.Add(new ProjectCapabilityInfo
            {
                Operation = operation,
                Applicability = containerKind == ProjectContainerKinds.LocalSession
                    ? ProjectCapabilityApplicabilities.LocalSessionConditional
                    : containerKind == ProjectContainerKinds.StandaloneProject
                        ? ProjectCapabilityApplicabilities.ProjectContent
                        : ProjectCapabilityApplicabilities.Unsupported
            });
        foreach (var operation in ProjectContentReadOperations.Concat(ProjectContentOperations))
            result.Add(new ProjectCapabilityInfo
            {
                Operation = operation,
                Applicability = containerKind == ProjectContainerKinds.ServerProject
                    ? ProjectCapabilityApplicabilities.NotYetDelivered
                    : ProjectCapabilityApplicabilities.ProjectContent
            });
        foreach (var operation in StandaloneLifecycleOperations)
            result.Add(new ProjectCapabilityInfo
            {
                Operation = operation,
                Applicability = ProjectCapabilityApplicabilities.StandaloneOnly
            });
        return result;
    }
}
