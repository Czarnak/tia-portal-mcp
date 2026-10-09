using TiaMcpServer.Contracts.Safety;

namespace TiaMcpServer.Contracts.Project;

public enum ProjectOpenDecision
{
    /// <summary>Operate on whatever is already attached.</summary>
    UseAttached,

    /// <summary>Nothing is attached and the requested project is not open.</summary>
    RequestedNotOpen,

    /// <summary>A different project is attached; refuse rather than open one alongside it.</summary>
    Refuse
}

/// <summary>
/// Decides whether a non-lifecycle operation may use TIA Portal's already-open project. Pure so
/// the net10.0 test project can cover it; the worker is net48 and references Siemens assemblies
/// the tests cannot load.
/// </summary>
public static class ProjectOpenPolicy
{
    public static ProjectOpenDecision Decide(string? currentPath, string? requestedPath)
    {
        var requested = ProjectPathNormalization.Canonicalize(requestedPath);
        if (requested is null)
        {
            return ProjectOpenDecision.UseAttached;
        }

        var current = ProjectPathNormalization.Canonicalize(currentPath);
        if (current is null)
        {
            return ProjectOpenDecision.RequestedNotOpen;
        }

        return string.Equals(current, requested, StringComparison.OrdinalIgnoreCase)
            ? ProjectOpenDecision.UseAttached
            : ProjectOpenDecision.Refuse;
    }

    public static string RefusalMessage(string currentPath, string requestedPath, McpAccessMode mode)
        => $"TIA Portal currently has project '{currentPath}' open, but this request targets "
            + $"'{requestedPath}'. This operation does not switch projects implicitly. Call bind_project with "
            + "the intended projectPath and forceRebind=true to select an already-open project."
            + (OperationPolicyCatalog.IsAllowed(mode, "open_project")
                ? " You can call open_project to switch."
                : " Open the intended project in TIA Portal before retrying.");

    public static string NotOpenMessage(string requestedPath, McpAccessMode mode)
    {
        var message = $"Requested project '{requestedPath}' is not open in TIA Portal. "
            + "Open the intended project in TIA Portal, then call bind_project with its projectPath.";
        return OperationPolicyCatalog.IsAllowed(mode, "open_project")
            ? message + " You can also call open_project."
            : message;
    }

    public static string NoProjectOpenMessage(McpAccessMode mode)
    {
        var message = "No project is open in TIA Portal. Open the intended project manually, "
            + "then call bind_project with its projectPath.";
        return OperationPolicyCatalog.IsAllowed(mode, "open_project")
            ? message + " You can also call open_project."
            : message;
    }
}
