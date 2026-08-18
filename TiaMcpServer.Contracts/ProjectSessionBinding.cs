using System;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Process-local project binding state. A configured path is only a caller assertion; guarded
/// writes require a complete identity observed from the worker and stored in the Verified state.
/// </summary>
public sealed class ProjectSessionBinding
{
    private readonly object _gate = new();
    private string? _configuredProjectPath;
    private WorkerSessionIdentity? _verifiedIdentity;
    private string _state;
    private string _bindingId = Guid.NewGuid().ToString("N");
    private long _revision;
    private string? _invalidatedReason;

    public ProjectSessionBinding(string? startupProjectPath)
    {
        _configuredProjectPath = ProjectPathNormalization.Canonicalize(startupProjectPath);
        _state = _configuredProjectPath is null
            ? ProjectBindingSnapshot.UnboundState
            : ProjectBindingSnapshot.ConfiguredUnverifiedState;
    }

    /// <summary>
    /// Effective path used for request routing. This may be an explicitly configured but not yet
    /// worker-verified path; use <see cref="TryGetVerified"/> before any guarded write.
    /// </summary>
    public string? BoundProjectPath
    {
        get
        {
            lock (_gate)
            {
                return _verifiedIdentity?.ProjectPath ?? _configuredProjectPath;
            }
        }
    }

    public string BindingState
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public bool IsVerified
    {
        get
        {
            lock (_gate)
            {
                return _state == ProjectBindingSnapshot.VerifiedState && _verifiedIdentity is not null;
            }
        }
    }

    private const string RebindInstruction =
        "Call open_project with forceRebind=true to rebind this session, or start a new MCP session for a different TIA project.";

    private static string AlreadyBoundError(string boundProjectPath, string requestedProjectPath)
        => $"This MCP session is already bound to project '{boundProjectPath}' and cannot use '{requestedProjectPath}'. {RebindInstruction}";

    public ProjectBindingSnapshot CaptureSnapshot()
    {
        lock (_gate)
        {
            return SnapshotNoLock();
        }
    }

    /// <summary>Reports whether a deliberate open/bind transition would be allowed.</summary>
    public bool CanBind(string? projectPath, bool forceRebind, out string? error)
    {
        lock (_gate)
        {
            error = null;
            var requested = Normalize(projectPath);
            if (requested is null)
            {
                error = "Project path is required.";
                return false;
            }

            if (_state == ProjectBindingSnapshot.InvalidatedState && !forceRebind)
            {
                error = $"This MCP session binding was invalidated ({_invalidatedReason ?? "session identity changed"}). {RebindInstruction}";
                return false;
            }

            var current = _verifiedIdentity?.ProjectPath ?? _configuredProjectPath;
            if (current is null || IsSameProject(current, requested) || forceRebind)
            {
                return true;
            }

            error = AlreadyBoundError(current, requested);
            return false;
        }
    }

    /// <summary>
    /// Resolves request routing without adopting a caller path. Invalidated bindings fail closed.
    /// </summary>
    public bool TryResolve(string? requestedProjectPath, out string? effectiveProjectPath, out string? error)
        => TryResolveWithSnapshot(
            requestedProjectPath,
            out _,
            out effectiveProjectPath,
            out error);

    /// <summary>
    /// Atomically captures the binding revision and resolves the request path against that same
    /// revision. Callers use the returned snapshot as the exact worker precondition, avoiding a
    /// capture/resolve race when another request rebinds the host session.
    /// </summary>
    public bool TryResolveWithSnapshot(
        string? requestedProjectPath,
        out ProjectBindingSnapshot snapshot,
        out string? effectiveProjectPath,
        out string? error)
    {
        lock (_gate)
        {
            snapshot = SnapshotNoLock();
            effectiveProjectPath = null;
            error = null;

            if (_state == ProjectBindingSnapshot.InvalidatedState)
            {
                error = $"This MCP session binding was invalidated ({_invalidatedReason ?? "session identity changed"}). {RebindInstruction}";
                return false;
            }

            var requested = Normalize(requestedProjectPath);
            var current = _verifiedIdentity?.ProjectPath ?? _configuredProjectPath;
            if (requested is null)
            {
                effectiveProjectPath = current;
                return true;
            }

            if (current is null)
            {
                // A request path alone is not a binding. Only a configured startup path or a
                // successful explicit lifecycle transition can establish one.
                effectiveProjectPath = requested;
                return true;
            }

            if (IsSameProject(current, requested))
            {
                effectiveProjectPath = current;
                return true;
            }

            error = AlreadyBoundError(current, requested);
            return false;
        }
    }

    /// <summary>
    /// Backward-compatible path assertion. It creates ConfiguredUnverified state; it is not
    /// sufficient for a guarded write until <see cref="TryPromoteConfigured"/> succeeds.
    /// </summary>
    public bool Bind(string projectPath, bool forceRebind, out string? error)
    {
        lock (_gate)
        {
            if (!CanBind(projectPath, forceRebind, out error))
            {
                return false;
            }

            var canonical = ProjectPathNormalization.Canonicalize(projectPath);
            if (canonical is null)
            {
                error = "Project path is required.";
                return false;
            }

            var current = _verifiedIdentity?.ProjectPath ?? _configuredProjectPath;
            if ((_state == ProjectBindingSnapshot.ConfiguredUnverifiedState ||
                 _state == ProjectBindingSnapshot.VerifiedState) &&
                current is not null &&
                IsSameProject(current, canonical))
            {
                // Reasserting the same path must not discard a complete worker identity. A real
                // rebind (different path or forceRebind) still creates a new unverified revision.
                return true;
            }

            _configuredProjectPath = canonical;
            _verifiedIdentity = null;
            TransitionTo(ProjectBindingSnapshot.ConfiguredUnverifiedState, invalidatedReason: null);
            return true;
        }
    }

    /// <summary>Binds atomically to worker-reported ground truth after open/create/save-as.</summary>
    public bool BindVerified(WorkerSessionIdentity? identity, bool forceRebind, out string? error)
    {
        lock (_gate)
        {
            if (!TryValidateCompleteIdentity(identity, out var canonicalPath, out error))
            {
                return false;
            }

            if (!CanBind(canonicalPath, forceRebind, out error))
            {
                return false;
            }

            SetVerified(identity!, canonicalPath!);
            return true;
        }
    }

    /// <summary>
    /// Promotes only an explicitly configured path after the worker reports the same project and
    /// a complete session identity. An ordinary unbound read can never bind through this method.
    /// </summary>
    public bool TryPromoteConfigured(WorkerSessionIdentity? identity, out string? error)
    {
        lock (_gate)
        {
            error = null;
            if (_state == ProjectBindingSnapshot.VerifiedState && _verifiedIdentity is not null)
            {
                if (!TryValidateCompleteIdentity(identity, out var verifiedPath, out error))
                {
                    return false;
                }

                if (SameIdentity(_verifiedIdentity, identity!, verifiedPath!))
                {
                    // Two concurrent status probes may both have observed the same configured
                    // revision. Accept the second identical response instead of invalidating the
                    // binding that the first probe has just verified.
                    return true;
                }

                error = "The configured project was already verified against a different worker session identity.";
                return false;
            }

            if (_state != ProjectBindingSnapshot.ConfiguredUnverifiedState || _configuredProjectPath is null)
            {
                error = "No explicitly configured project path is waiting for worker verification.";
                return false;
            }

            if (!TryValidateCompleteIdentity(identity, out var canonicalPath, out error))
            {
                return false;
            }

            if (!IsSameProject(_configuredProjectPath, canonicalPath!))
            {
                error = AlreadyBoundError(_configuredProjectPath, canonicalPath!);
                return false;
            }

            SetVerified(identity!, canonicalPath!);
            return true;
        }
    }

    /// <summary>Returns the exact verified binding required for project-mutating operations.</summary>
    public bool TryGetVerified(
        string? requestedProjectPath,
        out ProjectBindingSnapshot? binding,
        out string? error)
    {
        lock (_gate)
        {
            binding = null;
            error = null;

            if (_state == ProjectBindingSnapshot.InvalidatedState)
            {
                error = $"The verified project binding was invalidated ({_invalidatedReason ?? "session identity changed"}). {RebindInstruction}";
                return false;
            }

            if (_state != ProjectBindingSnapshot.VerifiedState || _verifiedIdentity is null)
            {
                error = "A worker-verified project binding is required before previewing or executing a write. "
                    + "Configure --project and verify it, or call open_project explicitly first.";
                return false;
            }

            var requested = Normalize(requestedProjectPath);
            if (requested is not null && !IsSameProject(_verifiedIdentity.ProjectPath!, requested))
            {
                error = AlreadyBoundError(_verifiedIdentity.ProjectPath!, requested);
                return false;
            }

            binding = SnapshotNoLock();
            return true;
        }
    }

    public bool MatchesVerifiedIdentity(WorkerSessionIdentity? identity, out string? error)
    {
        lock (_gate)
        {
            error = null;
            if (_state != ProjectBindingSnapshot.VerifiedState || _verifiedIdentity is null)
            {
                error = "This MCP session has no verified project identity.";
                return false;
            }

            if (!TryValidateCompleteIdentity(identity, out var path, out error))
            {
                return false;
            }

            if (!SameIdentity(_verifiedIdentity, identity!, path!))
            {
                error = $"The worker session identity changed. Expected worker '{_verifiedIdentity.WorkerSessionId}', "
                    + $"generation {_verifiedIdentity.SessionGeneration}, Portal PID {_verifiedIdentity.PortalProcessId}, "
                    + $"project '{_verifiedIdentity.ProjectPath}', but observed worker '{identity!.WorkerSessionId}', "
                    + $"generation {identity.SessionGeneration}, Portal PID {identity.PortalProcessId}, project '{path}'.";
                return false;
            }

            return true;
        }
    }

    public void Invalidate(string reason)
    {
        lock (_gate)
        {
            _configuredProjectPath = _verifiedIdentity?.ProjectPath ?? _configuredProjectPath;
            _verifiedIdentity = null;
            TransitionTo(ProjectBindingSnapshot.InvalidatedState, reason);
        }
    }

    /// <summary>
    /// Invalidates only if the binding is still the exact revision that a failed request used.
    /// A late response from an older request must never invalidate a newer, valid rebind.
    /// </summary>
    public bool TryInvalidate(ProjectBindingSnapshot expected, string reason)
    {
        lock (_gate)
        {
            if (!expected.SameBinding(SnapshotNoLock()))
            {
                return false;
            }

            _configuredProjectPath = _verifiedIdentity?.ProjectPath ?? _configuredProjectPath;
            _verifiedIdentity = null;
            TransitionTo(ProjectBindingSnapshot.InvalidatedState, reason);
            return true;
        }
    }

    public bool IsBoundTo(string? projectPath)
    {
        lock (_gate)
        {
            var current = _verifiedIdentity?.ProjectPath ?? _configuredProjectPath;
            return current is not null && projectPath is not null && IsSameProject(current, projectPath);
        }
    }

    public bool Clear(string? projectPath, out string? error)
    {
        lock (_gate)
        {
            error = null;
            var requested = Normalize(projectPath);
            var current = _verifiedIdentity?.ProjectPath ?? _configuredProjectPath;
            if (requested is not null && current is not null && !IsSameProject(current, requested))
            {
                error = $"This MCP session is already bound to project '{current}' and cannot clear '{requested}'.";
                return false;
            }

            _configuredProjectPath = null;
            _verifiedIdentity = null;
            TransitionTo(ProjectBindingSnapshot.UnboundState, invalidatedReason: null);
            return true;
        }
    }

    private void SetVerified(WorkerSessionIdentity identity, string canonicalPath)
    {
        _configuredProjectPath = canonicalPath;
        _verifiedIdentity = new WorkerSessionIdentity
        {
            WorkerSessionId = identity.WorkerSessionId,
            SessionGeneration = identity.SessionGeneration,
            PortalProcessId = identity.PortalProcessId,
            ProjectPath = canonicalPath
        };
        TransitionTo(ProjectBindingSnapshot.VerifiedState, invalidatedReason: null);
    }

    private void TransitionTo(string state, string? invalidatedReason)
    {
        _state = state;
        _invalidatedReason = invalidatedReason;
        _revision = checked(_revision + 1);
        _bindingId = Guid.NewGuid().ToString("N");
    }

    private ProjectBindingSnapshot SnapshotNoLock()
        => new(
            _state,
            _bindingId,
            _revision,
            _verifiedIdentity?.ProjectPath ?? _configuredProjectPath,
            _verifiedIdentity?.WorkerSessionId,
            _verifiedIdentity is null ? null : _verifiedIdentity.SessionGeneration,
            _verifiedIdentity?.PortalProcessId,
            _invalidatedReason);

    private static bool TryValidateCompleteIdentity(
        WorkerSessionIdentity? identity,
        out string? canonicalPath,
        out string? error)
    {
        canonicalPath = null;
        error = null;
        if (identity is null ||
            string.IsNullOrWhiteSpace(identity.WorkerSessionId) ||
            identity.SessionGeneration < 0 ||
            identity.PortalProcessId is null ||
            identity.PortalProcessId <= 0)
        {
            error = "The worker did not return a complete workerSessionId/sessionGeneration/portalProcessId identity.";
            return false;
        }

        canonicalPath = ProjectPathNormalization.Canonicalize(identity.ProjectPath);
        if (canonicalPath is null)
        {
            error = "The worker did not return a project path for the completed project operation.";
            return false;
        }

        return true;
    }

    private static bool SameIdentity(
        WorkerSessionIdentity expected,
        WorkerSessionIdentity actual,
        string canonicalActualPath)
        => string.Equals(expected.WorkerSessionId, actual.WorkerSessionId, StringComparison.Ordinal)
           && expected.SessionGeneration == actual.SessionGeneration
           && expected.PortalProcessId == actual.PortalProcessId
           && IsSameProject(expected.ProjectPath!, canonicalActualPath);

    private static bool IsSameProject(string boundProjectPath, string requestedProjectPath)
        => string.Equals(
            ProjectPathNormalization.Canonicalize(boundProjectPath),
            ProjectPathNormalization.Canonicalize(requestedProjectPath),
            StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? projectPath)
        => string.IsNullOrWhiteSpace(projectPath) ? null : projectPath!.Trim();
}
