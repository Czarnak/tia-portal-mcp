using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Vci;

public static class VciWriteCatalog
{
    private sealed record Spec(string[] Required, string[] Optional);
    private static readonly IReadOnlyDictionary<string, Spec> Specs = new Dictionary<string, Spec>(StringComparer.Ordinal)
    {
        ["create_workspace"] = new(["rootPath"], []),
        ["delete_workspace"] = new([], []),
        ["export"] = new(["scope"], ["relativeDirectory", "format"]),
        ["sync_to_workspace"] = new(["scope"], []),
        ["sync_to_project"] = new(["scope"], []),
        ["delete_mapping"] = new(["scope"], []),
    };

    public static IReadOnlyList<string> OperationNames { get; } = Specs.Keys.ToArray();

    public static (bool IsValid, string Error, string Category) ValidateWrite(IReadOnlyList<VciOperationRequest>? operations)
    {
        if (operations is null || operations.Count is < 1 or > 50)
            return (false, "Batch must contain 1 to 50 operations.", WorkerFailureCategories.ValidationError);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        string? scopeCategory = null;
        foreach (var item in operations)
        {
            if (item is null) { errors.Add("Batch contains a null operation."); continue; }
            void Error(string message) => errors.Add($"Operation '{item.Operation}' (operationId '{item.OperationId}'): {message}");
            if (string.IsNullOrWhiteSpace(item.OperationId) || item.OperationId.Length > 256 || !ids.Add(item.OperationId))
                Error("operationId must be unique, nonblank and at most 256 characters.");
            if (item.Operation is null || !Specs.TryGetValue(item.Operation, out var spec))
            {
                Error(VciMethods.Read.Contains(VciMethods.ForOperation(item.Operation!))
                    ? "This is a read operation; use vci_read." : "Unknown VCI write operation.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(item.WorkspaceName)) Error("workspaceName is required and must be nonblank.");
            foreach (var (name, value) in new (string, object?)[]
            {
                ("scope", item.Scope), ("rootPath", item.RootPath), ("relativeDirectory", item.RelativeDirectory),
                ("format", item.Format), ("withStatus", item.WithStatus), ("offset", item.Offset), ("limit", item.Limit),
            })
            {
                if (value is not null && !spec.Required.Contains(name) && !spec.Optional.Contains(name))
                    Error($"'{name}' is not valid for this operation.");
                if (value is null && spec.Required.Contains(name)) Error($"'{name}' is required.");
            }
            if (item.Format is not null && string.IsNullOrWhiteSpace(item.Format)) Error("format must be nonblank when supplied.");
            if (item.RootPath is not null)
            {
                var check = VciPathPolicy.ValidateRoot(item.RootPath);
                if (!check.IsValid) Error(check.Reason!);
            }
            var relative = VciPathPolicy.ValidateRelativeSyntax(item.RelativeDirectory);
            if (!relative.IsValid) Error(relative.Reason!);
            try { VciScope.Validate(item.Scope); }
            catch (ProjectTreeSelectionException exception)
            {
                Error($"scope: {exception.Message}"); scopeCategory ??= exception.Category;
            }
        }
        if (operations.Where(item => item is not null && !string.IsNullOrWhiteSpace(item.ProjectPath))
            .Select(item => WriteProjectPaths.Normalize(item.ProjectPath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            errors.Add("All write operations in a batch must target the same project path.");
        return errors.Count == 0 ? (true, string.Empty, string.Empty)
            : (false, string.Join("\n", errors), scopeCategory ?? WorkerFailureCategories.ValidationError);
    }

    public static IReadOnlyList<string> ValidateAccessMode(IReadOnlyList<VciOperationRequest> operations, McpAccessMode mode)
        => operations.Where(item => item is not null && item.Operation is not null && Specs.ContainsKey(item.Operation)
                && !OperationPolicyCatalog.IsAllowed(mode, VciMethods.ForOperation(item.Operation)))
            .Select(item => $"Operation '{item.Operation}' (operationId '{item.OperationId}') is not permitted in {McpAccessModeNames.ToName(mode)} mode.").ToArray();

    public static VciWriteRequestInfo ToWorkerRequest(VciOperationRequest item, string requestId, string breadcrumbPath) => new()
    {
        OperationId = item.OperationId, Operation = item.Operation, WorkspaceName = item.WorkspaceName,
        Scope = item.Scope, RootPath = item.RootPath, RelativeDirectory = item.RelativeDirectory,
        Format = item.Format, RequestId = requestId, BreadcrumbPath = breadcrumbPath,
    };
}
