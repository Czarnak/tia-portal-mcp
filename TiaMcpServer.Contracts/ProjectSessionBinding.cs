using System;

namespace TiaMcpServer.Contracts;

public sealed class ProjectSessionBinding
{
    private string? _boundProjectPath;

    public ProjectSessionBinding(string? startupProjectPath)
    {
        _boundProjectPath = ProjectPathNormalization.Canonicalize(startupProjectPath);
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
            IsSameProject(_boundProjectPath, requested) ||
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

        if (IsSameProject(_boundProjectPath, requested))
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

        _boundProjectPath = ProjectPathNormalization.Canonicalize(projectPath);
        return true;
    }

    public bool Clear(string? projectPath, out string? error)
    {
        error = null;

        var requested = Normalize(projectPath);
        if (requested is not null &&
            _boundProjectPath is not null &&
            !IsSameProject(_boundProjectPath, requested))
        {
            error = $"This MCP session is already bound to project '{_boundProjectPath}' and cannot clear '{requested}'.";
            return false;
        }

        _boundProjectPath = null;
        return true;
    }

    /// <summary>
    /// True when both paths identify the same project once canonicalized (relative segments
    /// resolved, separators normalized), compared case-insensitively. <paramref name="requestedProjectPath"/>
    /// arrives here only trimmed (see <see cref="Normalize"/>), so this is where the actual
    /// "same project?" comparison happens - not a second normalization pass on the bound value's
    /// storage, which is already canonical (see <see cref="Bind"/> and the constructor).
    /// </summary>
    private static bool IsSameProject(string boundProjectPath, string requestedProjectPath)
        => string.Equals(
            ProjectPathNormalization.Canonicalize(boundProjectPath),
            ProjectPathNormalization.Canonicalize(requestedProjectPath),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Trims only - deliberately not canonicalized. This is the value forwarded to the worker
    /// when the session is unbound (<see cref="TryResolve"/>) and the value used for null-checks
    /// throughout; canonicalizing it would change what gets sent over the wire for every unbound
    /// call, not just what gets compared. <see cref="IsSameProject"/> is where canonical
    /// comparison actually happens.
    /// </summary>
    private static string? Normalize(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return null;
        }

        return projectPath!.Trim();
    }
}
