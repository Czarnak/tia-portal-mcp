using System.Text.Json;
using TiaMcpServer.ProjectLifecycle;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Worker;

public partial class OpennessWorkerClient
{
    public Task<WorkerCallResult> GetProjectStatusAsync(string? projectPath)
    {
        // BindingTransition.None (the default): a direct status read never binds an unbound
        // session. It may promote an explicitly configured path after an exact identity match;
        // divergence from a verified/configured target is a hard binding failure.
        return SendBoundProjectRequestAsync(
            "get_project_status",
            projectPath,
            _ => { },
            "{}");
    }

    /// <summary>
    /// Internal state read used only by save/save-as/archive/close preview and apply-time
    /// current-state checks. The worker only inspects a project that is already open; it never
    /// opens one for this probe. Exactly like <see cref="GetProjectStatusAsync"/>, this
    /// host-side call is <see cref="BindingTransition.None"/>: an unbound session stays
    /// unbound even on success. Never exposed as an MCP tool; callable only from
    /// <c>ProjectLifecycleTools</c>'s own lifecycle-write implementations.
    /// </summary>
    internal Task<WorkerCallResult> ProbeProjectStatusForLifecycleAsync(string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "probe_project_status_for_lifecycle",
            projectPath,
            _ => { },
            "{}");
    }

    /// <summary>
    /// Reads the live source state for an open-project rebind without changing the binding.
    /// ProjectPath remains the verified source; the proposed destination travels separately.
    /// </summary>
    internal async Task<WorkerCallResult> ProbeOpenProjectRebindAsync(
        string sourceProjectPath,
        string destinationProjectPath)
    {
        var source = ProjectPathNormalization.Canonicalize(sourceProjectPath);
        var destination = ProjectPathNormalization.Canonicalize(destinationProjectPath);
        if (source is null || destination is null)
        {
            return WorkerCallResult.Fail(
                WorkerFailureCategories.ValidationError,
                "Source and destination project paths are required for the rebind probe.");
        }

        var result = await SendBoundProjectRequestAsync(
            "probe_open_project_rebind",
            source,
            request => request.RebindDestinationProjectPath = destination,
            "{}",
            BindingTransition.None).ConfigureAwait(false);
        if (!result.Success)
        {
            return result;
        }

        try
        {
            ProjectRebindStatePayloadContract.Decode(result.Payload, source, destination);
            return result;
        }
        catch (JsonException)
        {
            // A malformed worker payload is a protocol failure, never a caller-visible echo.
        }

        return WorkerCallResult.Fail(
            WorkerFailureCategories.ProtocolError,
            "The rebind-state probe returned an invalid snapshot.");
    }

    /// <summary>
    /// Internal basic-status read used only for lifecycle post-write verification (open / create /
    /// save / save-as / archive apply paths). Backed by the worker's
    /// <c>get_basic_project_status</c> operation, which returns the plain
    /// <see cref="ProjectStatusInfo"/> with no extended metadata - so a lifecycle write never
    /// enumerates history, queries the V21 settings providers, or surfaces metadata warnings
    /// after the write. Exactly like <see cref="GetProjectStatusAsync"/> and
    /// <see cref="ProbeProjectStatusForLifecycleAsync"/>, this is <see cref="BindingTransition.None"/>.
    /// Never exposed as an MCP tool; callable only from lifecycle-write apply paths.
    /// </summary>
    internal Task<WorkerCallResult> GetBasicProjectStatusAsync(string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "get_basic_project_status",
            projectPath,
            _ => { },
            "{}");
    }

    public Task<WorkerCallResult> OpenProjectAsync(string projectPath, bool forceRebind)
        => ExecuteSerializedBindingOperationAsync(
            () => OpenProjectCoreAsync(projectPath, forceRebind));

    public WorkerCallResult CheckOpenProjectBinding(string projectPath, bool forceRebind)
    {
        // A blank/whitespace path is caller input error, not a binding conflict — check it
        // separately so CanBind's single out-string ("Project path is required." vs. an
        // already-bound conflict) isn't collapsed into one category by inferring from its text.
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, "Project path is required.");
        }

        var current = BindingSnapshot;
        if (current.IsVerified && current.Context is { OpenedByWorker: true } local
            && string.Equals(ProjectPathNormalization.Canonicalize(local.SessionContainerPath),
                ProjectPathNormalization.Canonicalize(projectPath), StringComparison.OrdinalIgnoreCase))
            return WorkerCallResult.Ok("{}");

        // Upfront gate: the generic helper's TryResolveWithSnapshot has no forceRebind concept, so open keeps
        // its own binding-policy check against the CALLER's requested path before doing any work.
        if (!_projectSessionBinding.CanBind(projectPath, forceRebind, out var bindingError))
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict, bindingError!);
        }

        return WorkerCallResult.Ok("{}");
    }

    private async Task<WorkerCallResult> OpenProjectCoreAsync(string projectPath, bool forceRebind)
    {
        var bindingCheck = CheckOpenProjectBinding(projectPath, forceRebind);
        if (!bindingCheck.Success) return bindingCheck;

        var currentBinding = _projectSessionBinding.CaptureSnapshot();
        var bindingBeforeCall = _bindingOperationContext.Value?.PinnedBinding ?? currentBinding;
        if (CheckPinnedBinding(_bindingOperationContext.Value?.PinnedBinding, currentBinding) is { } failure)
        {
            return failure;
        }
        WorkerSessionIdentity? emptyIdentity = null;
        if (_emptyPortalRecovery is { } recovery && recovery.Binding.SameBinding(bindingBeforeCall))
        {
            var empty = await RevalidateRecoveredEmptyPortalAsync(bindingBeforeCall).ConfigureAwait(false);
            if (!empty.Success) return empty with { DispatchState = WorkerDispatchState.NotSent };
            emptyIdentity = recovery.Identity;
        }
        var result = await InvokeWorkerAsync(
            new WorkerRequest
            {
                Method = "open_project",
                ProjectPath = projectPath,
                ExpectedSessionIdentity = emptyIdentity ?? bindingBeforeCall.ToWorkerIdentity(),
            }).ConfigureAwait(false);

        if (!result.Success)
        {
            if (result.DispatchState == WorkerDispatchState.Sent && emptyIdentity is not null)
                _projectSessionBinding.TryInvalidate(bindingBeforeCall,
                    "The sent opener failed without proving the recovered empty Portal remained unchanged.");
            else if (result.DispatchState == WorkerDispatchState.Sent
                && string.Equals(Path.GetExtension(projectPath), ".als21", StringComparison.OrdinalIgnoreCase))
                // A failed response does not prove that the source survived the attempted replacement.
                InvalidateVerifiedBinding("the sent ALS opener failed without proving source continuity");
            return result;
        }

        if (emptyIdentity is not null && (result.SessionIdentity is not { } openedIdentity
            || openedIdentity.WorkerSessionId != emptyIdentity.WorkerSessionId
            || openedIdentity.PortalProcessId != emptyIdentity.PortalProcessId
            || openedIdentity.SessionGeneration <= emptyIdentity.SessionGeneration))
        {
            _projectSessionBinding.TryInvalidate(bindingBeforeCall, "The opener did not preserve the recovered Portal identity.");
            return WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict,
                "The opener did not preserve the recovered Portal identity. Inspect the possible mutation before retrying.", result.Warnings)
                with { DispatchState = result.DispatchState, IsPostOperationFailure = true };
        }

        if (string.Equals(Path.GetExtension(projectPath), ".als21", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var opened = LifecyclePayloadContract.Decode(result, "open_project", null);
                if (opened.Project?.Context?.OpenedByWorker != true
                    || !LifecyclePayloadContract.SamePath(opened.Project.Context.SessionContainerPath, projectPath))
                    throw new JsonException(LifecyclePayloadContract.ProtocolFailureMessage);
            }
            catch (JsonException)
            {
                InvalidateVerifiedBinding("the ALS opener returned contradictory owner evidence");
                return WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError,
                    LifecyclePayloadContract.ProtocolFailureMessage, result.Warnings) with
                {
                    IsPostOperationFailure = true,
                    DispatchState = result.DispatchState
                };
            }
        }

        // Bind to the project the worker actually opened, never the caller's projectPath argument.
        result = ApplyBindingTransition(
            BindingTransition.BindResolvedPath,
            result,
            bindingBeforeCall,
            bindForceRebind: forceRebind);

        if (!result.Success)
        {
            return result;
        }

        return string.IsNullOrEmpty(result.Payload) ? result with { Payload = "{}" } : result;
    }

    public Task<WorkerCallResult> CreateProjectAsync(
        string projectDirectory,
        string projectName,
        string? author,
        string? comment)
        => ExecuteSerializedBindingOperationAsync(
            () => CreateProjectCoreAsync(projectDirectory, projectName, author, comment));

    private async Task<WorkerCallResult> CreateProjectCoreAsync(
        string projectDirectory,
        string projectName,
        string? author,
        string? comment)
    {
        var currentBinding = _projectSessionBinding.CaptureSnapshot();
        var bindingBeforeCall = _bindingOperationContext.Value?.PinnedBinding ?? currentBinding;
        if (CheckPinnedBinding(_bindingOperationContext.Value?.PinnedBinding, currentBinding) is { } failure)
        {
            return failure;
        }
        var result = await InvokeWorkerAsync(
            new WorkerRequest
            {
                Method = "create_project",
                ExpectedSessionIdentity = bindingBeforeCall.ToWorkerIdentity(),
                ProjectDirectory = projectDirectory,
                ProjectName = projectName,
                Author = author,
                Comment = comment,
            }).ConfigureAwait(false);

        if (!result.Success)
        {
            return result;
        }

        // Bind to the project the worker actually created (its ResolvedProjectPath), never a path
        // parsed from payload text or reconstructed from the caller's directory/name. A newly
        // created project is a fresh binding target, so force-rebind past any prior binding.
        result = ApplyBindingTransition(
            BindingTransition.BindResolvedPath,
            result,
            bindingBeforeCall,
            bindForceRebind: true);

        if (!result.Success)
        {
            return result;
        }

        return string.IsNullOrEmpty(result.Payload) ? result with { Payload = "{}" } : result;
    }

    public Task<WorkerCallResult> SaveProjectAsync()
    {
        return SendBoundProjectRequestAsync(
            "save_project",
            null,
            _ => { },
            "{}");
    }

    /// <summary>
    /// Shared rejection message for the unsupported <c>save_project_as(rebind:false)</c> mode.
    /// Referenced by both this client's guard and <c>ProjectLifecycleTools.SaveProjectAs</c> so
    /// the two host-side defenses speak with one voice.
    /// </summary>
    internal const string RebindFalseUnsupportedMessage =
        "save_project_as requires rebind=true. The rebind=false mode is not supported: Siemens "
        + "SaveAs switches the active project to the copy, so a non-rebinding save would leave the "
        + "TIA Openness worker and this MCP session bound to different projects.";

    public Task<WorkerCallResult> SaveProjectAsAsync(
        string targetDirectory,
        string targetName,
        bool rebind)
    {
        // Defense in depth (mirrors the tool-layer guard): rebind=false is rejected before any
        // worker invocation, so it can never reach the transport or mutate the session binding.
        if (!rebind)
        {
            return Task.FromResult(
                WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, RebindFalseUnsupportedMessage));
        }

        // Past the guard rebind is always true: the worker opens the copy before this call
        // returns, so the session adopts the worker's ResolvedProjectPath (never payload text) and
        // gets no divergence warning. Task 4 tightens the worker-side copied-path guarantees behind
        // that ResolvedProjectPath.
        return SendBoundProjectRequestAsync(
            "save_project_as",
            null,
            request =>
            {
                request.TargetDirectory = targetDirectory;
                request.TargetName = targetName;
                request.Rebind = true;
            },
            "{}",
            BindingTransition.BindResolvedPath);
    }

    public Task<WorkerCallResult> ArchiveProjectAsync(
        string archiveDirectory,
        string archiveName,
        string? mode,
        bool saveBeforeArchive)
    {
        if (!ArchiveModeNames.TryNormalize(mode, out var normalizedMode, out var modeError))
        {
            return Task.FromResult(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, modeError!));
        }

        return SendBoundProjectRequestAsync(
            "archive_project",
            null,
            request =>
            {
                request.ArchiveDirectory = archiveDirectory;
                request.ArchiveName = archiveName;
                request.ArchiveMode = normalizedMode;
                request.SaveBeforeArchive = saveBeforeArchive;
            },
            "{}");
    }

    public Task<WorkerCallResult> CloseProjectAsync(bool saveBeforeClose)
    {
        return SendBoundProjectRequestAsync(
            "close_project",
            null,
            request =>
            {
                request.SaveBeforeClose = saveBeforeClose;
            },
            "{}",
            BindingTransition.Clear);
    }
}
