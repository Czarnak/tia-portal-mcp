namespace TiaMcpServer.Contracts;

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

    public static string RefusalMessage(string currentPath, string requestedPath)
        => $"TIA Portal currently has project '{currentPath}' open, but this request targets "
            + $"'{requestedPath}'. Read operations never switch projects. Omit projectPath to use "
            + "the open project, or call open_project to switch.";

    public static string NotOpenMessage(string requestedPath, McpAccessMode mode)
    {
        var message = $"Requested project '{requestedPath}' is not open in TIA Portal. "
            + "Open the intended project in TIA Portal and retry.";
        return OperationPolicyCatalog.IsAllowed(mode, "open_project")
            ? message + " You can also call open_project."
            : message;
    }
}
