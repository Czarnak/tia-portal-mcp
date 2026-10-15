namespace TiaMcpServer.Contracts.Vci;

public static class VciMethods
{
    public static IReadOnlyList<string> Read { get; } = Array.AsReadOnly(new[]
    {
        "vci_list_workspaces",
        "vci_list_mappings",
        "vci_supported_formats"
    });

    public const string Plan = "vci_plan";

    public static IReadOnlyList<string> Write { get; } = Array.AsReadOnly(new[]
    {
        "vci_create_workspace",
        "vci_delete_workspace",
        "vci_export",
        "vci_sync_to_workspace",
        "vci_sync_to_project",
        "vci_delete_mapping"
    });

    public static string ForOperation(string operation) => "vci_" + operation;
}
