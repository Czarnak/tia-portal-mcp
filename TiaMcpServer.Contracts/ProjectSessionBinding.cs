using System;

namespace TiaMcpServer.Contracts;

public sealed class ProjectSessionBinding
{
    private string? _boundProjectPath;

    public ProjectSessionBinding(string? startupProjectPath)
    {
        _boundProjectPath = Normalize(startupProjectPath);
    }

    public string? BoundProjectPath => _boundProjectPath;

    private const string RebindInstruction =
        "Call open_project with forceRebind=true to rebind this session, or start a new MCP session for a different TIA project.";

    private static string AlreadyBoundError(string boundProjectPath, string requestedProjectPath)
        => $"This MCP session is already bound to project '{boundProjectPath}' and cannot use '{requestedProjectPath}'. {RebindInstruction}";

    /// <summary>
    /// Reports whether <see cref="Bind"/> would succeed, without mutating the binding.
    /// Callers that must validate before doing expensive work use this; the error text is
    /// identical to the one <see cref="Bind"/> would produce.
    /// </summary>
    public bool CanBind(string? projectPath, bool forceRebind, out string? error)
    {
        error = null;

        var requested = Normalize(projectPath);
        if (requested is null)
        {
            error = "Project path is required.";
            return false;
        }

        if (_boundProjectPath is null ||
            string.Equals(_boundProjectPath, requested, StringComparison.OrdinalIgnoreCase) ||
            forceRebind)
        {
            return true;
        }

        error = AlreadyBoundError(_boundProjectPath, requested);
        return false;
    }

    public bool TryResolve(string? requestedProjectPath, out string? effectiveProjectPath, out string? error)
    {
        effectiveProjectPath = null;
        error = null;

        var requested = Normalize(requestedProjectPath);
        if (requested is null)
        {
            effectiveProjectPath = _boundProjectPath;
            return true;
        }

        if (_boundProjectPath is null)
        {
            // Deliberately does NOT adopt: a mistyped-but-real path must not retarget the session.
            // OpennessWorkerClient binds after the call succeeds, using the worker-reported path.
            effectiveProjectPath = requested;
            return true;
        }

        if (string.Equals(_boundProjectPath, requested, StringComparison.OrdinalIgnoreCase))
        {
            effectiveProjectPath = _boundProjectPath;
            return true;
        }

        error = AlreadyBoundError(_boundProjectPath, requested);
        return false;
    }

    public bool Bind(string projectPath, bool forceRebind, out string? error)
    {
        if (!CanBind(projectPath, forceRebind, out error))
        {
            return false;
        }

        _boundProjectPath = Normalize(projectPath);
        return true;
    }

    public bool Clear(string? projectPath, out string? error)
    {
        error = null;

        var requested = Normalize(projectPath);
        if (requested is not null &&
            _boundProjectPath is not null &&
            !string.Equals(_boundProjectPath, requested, StringComparison.OrdinalIgnoreCase))
        {
            error = $"This MCP session is already bound to project '{_boundProjectPath}' and cannot clear '{requested}'.";
            return false;
        }

        _boundProjectPath = null;
        return true;
    }

    private static string? Normalize(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return null;
        }

        return projectPath!.Trim();
    }
}
