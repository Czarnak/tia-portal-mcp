using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Vci;

public static class VciReadCatalog
{
    private static readonly IReadOnlyDictionary<string, string[]> Fields = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["list_workspaces"] = [],
        ["list_mappings"] = ["workspaceName", "scope", "withStatus", "offset", "limit"],
        ["supported_formats"] = ["workspaceName", "scope"],
    };

    public static (bool IsValid, string Error, string Category) ValidateRead(IReadOnlyList<VciOperationRequest>? operations)
    {
        if (operations is null || operations.Count is < 1 or > 50)
            return (false, "Batch must contain 1 to 50 operations.", WorkerFailureCategories.ValidationError);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        string? scopeCategory = null;
        foreach (var item in operations)
        {
            if (item is null) { errors.Add("Batch contains a null operation."); continue; }
            if (string.IsNullOrWhiteSpace(item.OperationId) || item.OperationId.Length > 256 || !ids.Add(item.OperationId))
                errors.Add("Each operationId must be unique, nonblank and at most 256 characters.");
            if (string.IsNullOrWhiteSpace(item.Operation)) { errors.Add("Operation name is required."); continue; }
            if (!Fields.TryGetValue(item.Operation, out var allowed))
            {
                errors.Add(VciMethods.Write.Contains(VciMethods.ForOperation(item.Operation))
                    ? $"'{item.Operation}' is a write operation; use vci_write."
                    : $"Unknown VCI read operation '{item.Operation}'.");
                continue;
            }
            foreach (var (name, present) in new (string, bool)[]
            {
                ("workspaceName", item.WorkspaceName is not null), ("scope", item.Scope is not null),
                ("withStatus", item.WithStatus is not null), ("offset", item.Offset is not null), ("limit", item.Limit is not null),
                ("rootPath", item.RootPath is not null), ("relativeDirectory", item.RelativeDirectory is not null), ("format", item.Format is not null),
            })
                if (present && !allowed.Contains(name)) errors.Add($"'{name}' is not valid for {item.Operation}.");
            if (item.Operation == "list_mappings" && string.IsNullOrWhiteSpace(item.WorkspaceName))
                errors.Add("list_mappings requires workspaceName.");
            if (item.WorkspaceName is not null && string.IsNullOrWhiteSpace(item.WorkspaceName))
                errors.Add("workspaceName must be nonblank.");
            if (item.Offset < 0) errors.Add("offset must be nonnegative.");
            if (item.Limit is < 1 or > 200) errors.Add("limit must be 1 to 200.");
            try { VciScope.Validate(item.Scope); }
            catch (ProjectTreeSelectionException exception)
            {
                errors.Add($"scope: {exception.Message}");
                scopeCategory ??= exception.Category;
            }
            if (item.Operation == "supported_formats" && (item.Scope is not { Count: > 0 } || item.Scope[^1] is null || !VciScope.IsLeaf(item.Scope[^1].NodeType)))
                errors.Add("supported_formats requires scope naming one terminal leaf.");
        }
        if (operations.Where(item => item is not null && !string.IsNullOrWhiteSpace(item.ProjectPath))
            .Select(item => item.ProjectPath!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            errors.Add("All operations must target the same project path.");
        return errors.Count == 0 ? (true, string.Empty, string.Empty)
            : (false, string.Join("\n", errors), scopeCategory ?? WorkerFailureCategories.ValidationError);
    }

    public static IReadOnlyList<string> ValidateAccessMode(IReadOnlyList<VciOperationRequest> operations, McpAccessMode mode)
        => operations.Where(item => !OperationPolicyCatalog.IsAllowed(mode, VciMethods.ForOperation(item.Operation)))
            .Select(item => $"Operation '{item.Operation}' is not permitted in {McpAccessModeNames.ToName(mode)} mode.").ToArray();

    public static VciQueryInfo ToQuery(VciOperationRequest item) => new()
    {
        WorkspaceName = item.WorkspaceName, Scope = item.Scope, WithStatus = item.WithStatus ?? false,
        Offset = item.Offset, Limit = item.Limit,
    };
}
