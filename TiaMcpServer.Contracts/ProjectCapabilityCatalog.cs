namespace TiaMcpServer.Contracts;

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

    private static readonly string[] ProjectContentOperations =
    {
        "browse_project_tree", "plc_read", "plc_write", "network_read", "network_write",
        "read_cross_references", "compile_check", "save_project",
        "get_block_content", "get_type_content", "list_tag_tables",
        "update_block_logic", "update_type_content", "create_tag_table", "delete_tag_table",
        "create_tag", "update_tag", "delete_tag", "create_user_constant",
        "update_user_constant", "delete_user_constant", "create_block", "delete_block",
        "create_block_group", "delete_block_group",
        "read_hardware_config", "list_network_objects", "inspect_network_object",
        "add_network_device", "configure_network_device", "create_subnet", "update_subnet",
        "delete_subnet"
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

    public static bool Supports(string containerKind, string operation)
    {
        if (string.IsNullOrWhiteSpace(operation)) return false;
        if (containerKind == ProjectContainerKinds.StandaloneProject)
            return Standalone.Any(item => item.Operation == operation);
        if (containerKind == ProjectContainerKinds.LocalSession)
            return BasicLocalOperations.Contains(operation, StringComparer.Ordinal);
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
        foreach (var operation in ProjectContentOperations)
            result.Add(new ProjectCapabilityInfo
            {
                Operation = operation,
                Applicability = containerKind == ProjectContainerKinds.StandaloneProject
                    ? ProjectCapabilityApplicabilities.ProjectContent
                    : ProjectCapabilityApplicabilities.NotYetDelivered
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
