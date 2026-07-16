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
            _boundProjectPath = requested;
            effectiveProjectPath = requested;
            return true;
        }

        if (string.Equals(_boundProjectPath, requested, StringComparison.OrdinalIgnoreCase))
        {
            effectiveProjectPath = _boundProjectPath;
            return true;
        }

        var boundProjectPath = _boundProjectPath ?? string.Empty;
        error = $"This MCP session is already bound to project '{boundProjectPath}' and cannot use '{requested}'. Call open_project with forceRebind=true to rebind this session, or start a new MCP session for a different TIA project.";
        return false;
    }

    public bool Bind(string projectPath, bool forceRebind, out string? error)
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
            _boundProjectPath = requested;
            return true;
        }

        var boundProjectPath = _boundProjectPath ?? string.Empty;
        error = $"This MCP session is already bound to project '{boundProjectPath}' and cannot use '{requested}'. Start a new MCP session for a different TIA project or set forceRebind=true.";
        return false;
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
