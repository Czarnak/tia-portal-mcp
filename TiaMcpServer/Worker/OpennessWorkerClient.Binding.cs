using System.Text.Json;
using Microsoft.Extensions.Logging;
using TiaMcpServer.Contracts.Diagnostics;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Worker;

public partial class OpennessWorkerClient
{
    /// <summary>Current immutable host binding snapshot, used by the guarded write pipeline.</summary>
    public ProjectBindingSnapshot BindingSnapshot => _projectSessionBinding.CaptureSnapshot();

    /// <summary>Inspects the persistent Portal attachment without selecting or promoting a project.</summary>
    public Task<ProjectInspectionOutcome> InspectPortalAsync(WorkerRequest request, CancellationToken cancellationToken = default)
        => ExecuteSerializedBindingOperationAsync(async () =>
        {
            var before = BindingSnapshot;
            IReadOnlyList<TiaPortalProcessInfo> portals = Array.Empty<TiaPortalProcessInfo>();
            ProjectInspectionOutcome Complete(WorkerCallResult result) => new(before, BindingSnapshot, result, portals);
            if (request.Method != "list_tia_portal_processes" && !Tools.ProjectBindingInspectionCatalog.IsInventory(request.Method))
                return Complete(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, "The requested operation is not a Portal inspection.")
                    with { DispatchState = WorkerDispatchState.NotSent });
            if (before.IsVerified && request.PortalProcessId is { } pid && pid != before.PortalProcessId)
                return Complete(WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict,
                    "Inventory is limited to the currently bound Portal. Use bind_project to select another open project.")
                    with { DispatchState = WorkerDispatchState.NotSent });

            var discovery = request.Method == "list_tia_portal_processes";
            // Copy only inspection selectors; configured/last-bound paths are not attachment authority.
            var sent = new WorkerRequest
            {
                Method = request.Method,
                PortalProcessId = discovery ? null : before.IsVerified ? before.PortalProcessId : request.PortalProcessId,
                ExpectedSessionIdentity = !discovery && before.IsVerified ? before.ToWorkerIdentity() : null,
                MultiuserServerAlias = request.MultiuserServerAlias,
                MultiuserGroupIsRoot = request.MultiuserGroupIsRoot,
                MultiuserGroupName = request.MultiuserGroupName,
                MultiuserServerProjectName = request.MultiuserServerProjectName
            };
            var result = await InvokeWorkerAsync(sent).ConfigureAwait(false);
            if (!discovery && before.IsVerified && result.Success)
                result = ValidateOrPromoteSessionIdentity(result, before);
            if (!discovery && result.Success && (result.PortalProcessId is null or <= 0
                || (sent.PortalProcessId is not null && result.PortalProcessId != sent.PortalProcessId)))
                result = WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError,
                    "The worker did not report the asserted Portal attachment.", result.Warnings)
                    with { DispatchState = result.DispatchState, IsPostOperationFailure = true };

            if (discovery && result.Success || !result.Success && result.FailureCategory is
                WorkerFailureCategories.TargetNotFound or WorkerFailureCategories.TargetAmbiguous)
            {
                var listing = discovery ? result : await InvokeWorkerAsync(new WorkerRequest { Method = "list_tia_portal_processes" }).ConfigureAwait(false);
                if (listing.Success)
                {
                    if (before.IsVerified)
                    {
                        var validated = ValidateOrPromoteSessionIdentity(listing, before);
                        if (!validated.Success) result = validated;
                    }
                    try
                    {
                        var inventory = ProjectBindingPayloadContract.DecodeProcessList(listing.Payload);
                        portals = inventory.Processes;
                        var invalidated = BindingSnapshot;
                        if (before.IsVerified && invalidated.State == ProjectBindingSnapshot.InvalidatedState
                            && invalidated.Revision == before.Revision + 1
                            && listing.ResolvedProjectPath is null
                            && listing.SessionIdentity is { ProjectPath: null, Context: null } empty
                            && empty.WorkerSessionId == before.WorkerSessionId
                            && empty.PortalProcessId == before.PortalProcessId
                            && empty.SessionGeneration > before.SessionGeneration
                            && listing.PortalProcessId == empty.PortalProcessId
                            && inventory.AttachedProcessId == empty.PortalProcessId)
                            _emptyPortalRecovery = (invalidated, new WorkerSessionIdentity
                            {
                                WorkerSessionId = empty.WorkerSessionId, PortalProcessId = empty.PortalProcessId,
                                SessionGeneration = empty.SessionGeneration
                            });
                    }
                    catch (JsonException)
                    {
                        result = WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError,
                            "The worker Portal listing did not match its declared contract.", result.Warnings)
                            with { DispatchState = listing.DispatchState, IsPostOperationFailure = true };
                    }
                }
                else result = listing;
            }
            return Complete(result);
        }, cancellationToken);

    /// <summary>Selects an already-open project under the serialized session binding gate.</summary>
    public Task<ProjectBindingOutcome> BindOpenProjectAsync(
        string? projectPath, bool forceRebind, CancellationToken cancellationToken = default,
        int? portalProcessId = null)
        => ExecuteSerializedBindingOperationAsync(
            () => BindOpenProjectCoreAsync(projectPath, forceRebind, cancellationToken, portalProcessId), cancellationToken);

    private async Task<ProjectBindingOutcome> BindOpenProjectCoreAsync(
        string? projectPath, bool forceRebind, CancellationToken cancellationToken, int? portalProcessId)
    {
        var before = BindingSnapshot;
        var invalidationTarget = before;
        IReadOnlyList<TiaPortalProcessInfo> portals = Array.Empty<TiaPortalProcessInfo>();
        var warnings = new List<string>();
        var dispatched = false;
        ProjectBindingOutcome Fail(WorkerCallResult failure, bool rejection = false)
        {
            if (!rejection && invalidationTarget.State != ProjectBindingSnapshot.UnboundState
                && !ProjectBindingDecision.PreDetachRefusalCategories.Contains(failure.FailureCategory ?? string.Empty))
                _projectSessionBinding.TryInvalidate(invalidationTarget, failure.Error ?? "Open-project binding failed.");
            return new(ProjectBindingTransitions.None, before, BindingSnapshot, null, portals,
                CapWarnings(warnings.Concat(failure.Warnings).ToArray()), failure, rejection);
        }
        async Task<WorkerCallResult> SendSelection(WorkerRequest request)
        {
            if (!dispatched) cancellationToken.ThrowIfCancellationRequested();
            dispatched = true;
            // Session selection is binding-neutral until strict payload/identity validation and CAS.
            var result = await InvokeWorkerAsync(request).ConfigureAwait(false);
            return result.Success
                ? ApplyBindingTransition(BindingTransition.BindingNeutral, result, before, false)
                : result;
        }
        async Task<WorkerCallResult?> List()
        {
            var listing = await SendSelection(new WorkerRequest { Method = "list_tia_portal_processes" }).ConfigureAwait(false);
            if (!listing.Success) return listing;
            warnings.AddRange(listing.Warnings);
            try { portals = ProjectBindingPayloadContract.DecodeProcessList(listing.Payload).Processes; return null; }
            catch (JsonException) { return WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError, "The worker Portal listing did not match its declared contract."); }
        }
        async Task<ProjectBindingOutcome> Verify(string transition, string path)
        {
            if (!dispatched) cancellationToken.ThrowIfCancellationRequested();
            dispatched = true;
            var status = await GetProjectStatusAsync(path).ConfigureAwait(false);
            if (!status.Success) return Fail(status);
            warnings.AddRange(status.Warnings);
            var decoded = Tools.StandalonePayloadContract.DecodeStatus(status);
            if (decoded.Failure is not null)
                return Fail(WorkerCallResult.Fail(decoded.Failure.Category, decoded.Failure.Message));
            var project = decoded.Value;
            if (project is null || !project.IsOpen || !string.Equals(
                ProjectPathNormalization.Canonicalize(project.Path), path, StringComparison.OrdinalIgnoreCase))
                return Fail(WorkerCallResult.Fail(WorkerFailureCategories.PostconditionFailed, "The verifying status did not report the selected open project."));
            project.Metadata = null;
            return new(transition, before, BindingSnapshot, project, portals, CapWarnings(warnings), null, false);
        }

        if (portalProcessId is <= 0)
            return Fail(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, "portalProcessId must be positive."), true);
        var assertedPath = projectPath ?? (before.State == ProjectBindingSnapshot.ConfiguredUnverifiedState
            ? before.ProjectPath : null);
        if (assertedPath is not null && Cli.ConfiguredProjectPath.Validate(assertedPath) is { } invalidPath)
            return Fail(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError,
                $"projectPath {invalidPath}"), true);
        var step = ProjectBindingDecision.Decide(before, projectPath, forceRebind, portalProcessId);
        if (step is BindingStep.Reject reject)
            return Fail(WorkerCallResult.Fail(reject.Category, reject.Message), true);
        if (step is BindingStep.Reverify)
            return await Verify(ProjectBindingTransitions.Unchanged, before.ProjectPath!).ConfigureAwait(false);
        if (step is BindingStep.ListThenSelect)
        {
            var failure = await List().ConfigureAwait(false);
            if (failure is not null) return Fail(failure);
            var choice = ProjectBindingDecision.ChooseFromListing(portals, portalProcessId);
            if (choice is not ListingChoice.Select selected)
                return Fail(WorkerCallResult.Fail(choice is ListingChoice.NotFound
                    ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                    "Select an open project from the reported Portal inventory."));
            step = new BindingStep.Select(selected.Path, false, selected.PortalProcessId);
        }
        var select = (BindingStep.Select)step;
        var result = await SendSelection(new WorkerRequest
        {
            Method = "select_portal_project", ProjectPath = select.Path,
            PortalProcessId = select.PortalProcessId,
            ExpectedSessionIdentity = before.IsVerified ? before.ToWorkerIdentity() : null
        }).ConfigureAwait(false);
        if (!result.Success)
        {
            if (result.FailureCategory is WorkerFailureCategories.TargetNotFound or WorkerFailureCategories.TargetAmbiguous)
            {
                var listingFailure = await List().ConfigureAwait(false);
                if (listingFailure is not null) return Fail(listingFailure);
            }
            return result.FailureCategory == WorkerFailureCategories.GuardBlocked
                ? Fail(result with { FailureCategory = WorkerFailureCategories.BindingConflict }, true)
                : Fail(result);
        }
        warnings.AddRange(result.Warnings);
        PortalProjectSelectionInfo selection;
        try { selection = ProjectBindingPayloadContract.DecodeSelection(result.Payload, before); }
        catch (JsonException) { return Fail(WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError, "The worker Portal selection did not match its declared contract.")); }
        if (!TryValidateCompleteSessionIdentity(result.SessionIdentity, out var identityPath)
            || (select.Path is not null && !string.Equals(identityPath, select.Path, StringComparison.OrdinalIgnoreCase))
            || (select.PortalProcessId is not null && result.SessionIdentity!.PortalProcessId != select.PortalProcessId))
            return Fail(WorkerCallResult.Fail(WorkerFailureCategories.PostconditionFailed, "The worker did not stamp the complete selected project identity."));
        if (!_projectSessionBinding.TryAdoptVerified(before, result.SessionIdentity, out var error))
            return Fail(WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict, error!));
        invalidationTarget = BindingSnapshot;
        RefreshAmbientPinnedBinding();
        if (selection.Reattached)
            warnings.Add("TIA Portal may have shown the Openness dialog. Answering Yes to all stops it for this worker binary.");
        if (selection.PreviousProjectWasWorkerOpened && selection.PreviousProjectIsModified == true)
            warnings.Add("The previous project opened by the worker remains open with unsaved changes. Save or discard them in TIA Portal.");
        return await Verify(select.IsSwitch ? ProjectBindingTransitions.Switched : ProjectBindingTransitions.Bound, identityPath!).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a guarded write critical section against an exact host binding
    /// revision. Nested worker requests inherit that pinned identity, and other requests on this
    /// client wait until the section completes. Cancellation can end a wait without dispatching.
    /// </summary>
    public async Task<PinnedBindingExecutionResult<T>> ExecuteWithPinnedBindingAsync<T>(
        ProjectBindingSnapshot? expectedBinding,
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedBinding is null)
        {
            return PinnedBindingExecutionResult<T>.Fail(WorkerCallResult.Fail(
                WorkerFailureCategories.BindingConflict,
                "The write did not retain a project session binding."));
        }

        var ambient = _bindingOperationContext.Value;
        if (ambient is not null)
        {
            if (ambient.PinnedBinding is not null &&
                !ambient.PinnedBinding.SameBinding(expectedBinding))
            {
                return PinnedBindingExecutionResult<T>.Fail(WorkerCallResult.Fail(
                    WorkerFailureCategories.BindingConflict,
                    "A nested operation attempted to use a different pinned project binding."));
            }

            return await ExecutePinnedCoreAsync(expectedBinding, operation, ambient)
                .ConfigureAwait(false);
        }

        await _bindingOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var previous = _bindingOperationContext.Value;
        var context = new BindingOperationContext(expectedBinding);
        _bindingOperationContext.Value = context;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecutePinnedCoreAsync(expectedBinding, operation, context)
                .ConfigureAwait(false);
        }
        finally
        {
            _bindingOperationContext.Value = previous;
            _bindingOperationGate.Release();
        }
    }

    private async Task<PinnedBindingExecutionResult<T>> ExecutePinnedCoreAsync<T>(
        ProjectBindingSnapshot expectedBinding,
        Func<Task<T>> operation,
        BindingOperationContext context)
        where T : class
    {
        var current = _projectSessionBinding.CaptureSnapshot();
        if (CheckPinnedBinding(expectedBinding, current) is { } failure)
        {
            return PinnedBindingExecutionResult<T>.Fail(failure);
        }

        var previousPinned = context.PinnedBinding;
        context.PinnedBinding = expectedBinding;
        try
        {
            return PinnedBindingExecutionResult<T>.Ok(
                await operation().ConfigureAwait(false));
        }
        finally
        {
            context.PinnedBinding = previousPinned;
        }
    }

    /// <summary>
    /// Fail-closed gate for every project-scoped call, read or write. A startup --project value is
    /// first grounded by one read-only status request; an ordinary unbound session is never
    /// adopted implicitly and must use bind_project, or open_project in read-write/full mode.
    /// </summary>
    public async Task<WorkerCallResult> RequireVerifiedBindingAsync()
    {
        if (_projectSessionBinding.TryGetVerified(null, out _, out _))
        {
            return WorkerCallResult.Ok("{}");
        }

        if (await GroundConfiguredProjectAsync().ConfigureAwait(false) is { } verification)
        {
            return verification;
        }

        return _projectSessionBinding.TryGetVerified(null, out _, out var error)
            ? WorkerCallResult.Ok("{}")
            : WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict, error!);
    }

    private static WorkerCallResult? CheckPinnedBinding(
        ProjectBindingSnapshot? pinnedBinding,
        ProjectBindingSnapshot currentBinding)
        => pinnedBinding is not null && !pinnedBinding.SameBinding(currentBinding)
            ? WorkerCallResult.Fail(
                WorkerFailureCategories.BindingConflict,
                "The worker/Portal/project binding changed after preview. No operation was performed; request a fresh preview.")
            : null;

    private async Task<WorkerCallResult?> GroundConfiguredProjectAsync()
    {
        var initial = _projectSessionBinding.CaptureSnapshot();
        if (string.Equals(initial.State, ProjectBindingSnapshot.ConfiguredUnverifiedState, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(initial.ProjectPath))
        {
            var verification = await GetProjectStatusAsync(initial.ProjectPath).ConfigureAwait(false);
            if (!verification.Success) return verification;
        }

        return null;
    }

    /// <summary>
    /// Re-grounds only the source retained by an invalidated binding before an explicit
    /// force-rebind preview. No destination path enters this read-only status route.
    /// </summary>
    internal Task<PinnedBindingExecutionResult<ProjectBindingSnapshot>> RegroundInvalidatedSourceForOpenAsync(
        bool forceRebind, ProjectBindingSnapshot? expectedBinding = null)
        => ExecuteSerializedBindingOperationAsync(async () =>
        {
            var invalidated = _projectSessionBinding.CaptureSnapshot();
            if (expectedBinding is not null && !expectedBinding.SameBinding(invalidated))
            {
                return PinnedBindingExecutionResult<ProjectBindingSnapshot>.Fail(WorkerCallResult.Fail(
                    WorkerFailureCategories.BindingConflict,
                    "The retained source binding changed before recovery could prepare it."));
            }
            if (!forceRebind || invalidated.State != ProjectBindingSnapshot.InvalidatedState)
            {
                return PinnedBindingExecutionResult<ProjectBindingSnapshot>.Fail(WorkerCallResult.Fail(
                    WorkerFailureCategories.BindingConflict,
                    "An invalidated source can be re-grounded by bind_project with forceRebind=true, "
                    + "or by open_project with forceRebind=true in read-write/full mode."));
            }

            var source = ProjectPathNormalization.Canonicalize(invalidated.ProjectPath);
            if (source is null)
            {
                return PinnedBindingExecutionResult<ProjectBindingSnapshot>.Fail(WorkerCallResult.Fail(
                    WorkerFailureCategories.BindingConflict,
                    "The invalidated binding has no retained source project to verify."));
            }

            if (_emptyPortalRecovery is { } recovery && recovery.Binding.SameBinding(invalidated))
                return await RecoverEmptyPortalAsync(invalidated, recovery.Identity).ConfigureAwait(false);

            // Reasserting retained A creates a fresh configured revision. The only request that
            // follows is the existing read-only status route. It must prove either the complete
            // matching source identity or an empty attachment before preparing the guarded open.
            if (!_projectSessionBinding.Bind(source, forceRebind: true, out var bindError))
            {
                return PinnedBindingExecutionResult<ProjectBindingSnapshot>.Fail(WorkerCallResult.Fail(
                    WorkerFailureCategories.BindingConflict,
                    bindError ?? "The retained source project could not be reasserted."));
            }

            var reasserted = _projectSessionBinding.CaptureSnapshot();
            var status = await InvokeWorkerAsync(new WorkerRequest
            {
                Method = "get_project_status", ProjectPath = source
            }).ConfigureAwait(false);
            if (!reasserted.SameBinding(BindingSnapshot))
                return PinnedBindingExecutionResult<ProjectBindingSnapshot>.Fail(WorkerCallResult.Fail(
                    WorkerFailureCategories.BindingConflict, "The source binding changed while its status was being verified."));
            // A restarted Portal can be attached without a project. Keep project promotion strict;
            // only a successful, coherent empty status may seed the separate zero-owner proof.
            if (status.Success && status.ResolvedProjectPath is null
                && status.SessionIdentity is { ProjectPath: null, Context: null, PortalProcessId: > 0, SessionGeneration: >= 0 } emptyIdentity
                && !string.IsNullOrWhiteSpace(emptyIdentity.WorkerSessionId)
                && status.PortalProcessId == emptyIdentity.PortalProcessId)
            {
                var decoded = Tools.StandalonePayloadContract.DecodeStatus(status);
                if (decoded.Value is { IsOpen: false, Path: null, Context: null })
                {
                    _emptyPortalRecovery = (reasserted, emptyIdentity);
                    var empty = await RecoverEmptyPortalAsync(reasserted, emptyIdentity).ConfigureAwait(false);
                    if (empty.Success) return empty;
                    status = empty.Failure!;
                }
                else status = WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError,
                    Tools.StandalonePayloadContract.ProtocolFailureMessage, status.Warnings);
            }
            else if (status.Success)
                status = ValidateOrPromoteSessionIdentity(status, reasserted);
            if (!status.Success)
            {
                // Status may already have invalidated the binding (for example an incomplete
                // identity). Otherwise restore invalidated from precisely our reasserted
                // revision, retaining the original worker failure category for the caller.
                var afterFailure = _projectSessionBinding.CaptureSnapshot();
                if (afterFailure.State != ProjectBindingSnapshot.InvalidatedState &&
                    !_projectSessionBinding.TryInvalidate(
                        reasserted, "Retained-source status verification failed."))
                {
                    return PinnedBindingExecutionResult<ProjectBindingSnapshot>.Fail(WorkerCallResult.Fail(
                        WorkerFailureCategories.BindingConflict,
                        "The source binding changed while its status was being verified."));
                }

                return PinnedBindingExecutionResult<ProjectBindingSnapshot>.Fail(status);
            }

            var promoted = _projectSessionBinding.CaptureSnapshot();
            if (!promoted.IsVerified ||
                !string.Equals(
                    ProjectPathNormalization.Canonicalize(promoted.ProjectPath),
                    source,
                    StringComparison.OrdinalIgnoreCase) ||
                promoted.ToWorkerIdentity() is null)
            {
                if (promoted.State != ProjectBindingSnapshot.InvalidatedState)
                {
                    _projectSessionBinding.TryInvalidate(
                        promoted, "Retained-source status did not establish a complete identity.");
                }
                return PinnedBindingExecutionResult<ProjectBindingSnapshot>.Fail(WorkerCallResult.Fail(
                    WorkerFailureCategories.PostconditionFailed,
                    "Retained-source status did not establish a complete source identity."));
            }

            return PinnedBindingExecutionResult<ProjectBindingSnapshot>.Ok(promoted);
        });

    private async Task<PinnedBindingExecutionResult<ProjectBindingSnapshot>> RecoverEmptyPortalAsync(
        ProjectBindingSnapshot expected, WorkerSessionIdentity identity)
    {
        var empty = await RevalidateRecoveredEmptyPortalAsync(expected).ConfigureAwait(false);
        if (!empty.Success) return PinnedBindingExecutionResult<ProjectBindingSnapshot>.Fail(empty);
        if (!_projectSessionBinding.TryClear(expected, out var unbound))
            return PinnedBindingExecutionResult<ProjectBindingSnapshot>.Fail(WorkerCallResult.Fail(
                WorkerFailureCategories.BindingConflict, "The source binding changed during empty-Portal recovery."));
        _emptyPortalRecovery = (unbound!, identity);
        return PinnedBindingExecutionResult<ProjectBindingSnapshot>.Ok(unbound!);
    }

    /// <summary>
    /// Rechecks only a previously observed empty attachment; ordinary unbound sessions stay unchanged.
    /// </summary>
    internal Task<WorkerCallResult> RevalidateRecoveredEmptyPortalAsync(ProjectBindingSnapshot expected)
        => ExecuteSerializedBindingOperationAsync(async () =>
        {
            if (!expected.SameBinding(BindingSnapshot))
                return WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict,
                    "The binding changed before empty-Portal verification.");
            if (_emptyPortalRecovery is not { } recovery || !recovery.Binding.SameBinding(expected))
                return WorkerCallResult.Ok("{}");

            var result = await InvokeWorkerAsync(new WorkerRequest
            {
                Method = "get_project_status", ExpectedSessionIdentity = recovery.Identity
            }).ConfigureAwait(false);
            if (!expected.SameBinding(BindingSnapshot))
                return WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict,
                    "The binding changed during empty-Portal verification.");
            if (result.Success)
            {
                var decoded = Tools.StandalonePayloadContract.DecodeStatus(result);
                if (decoded.Failure is { } failure)
                    result = WorkerCallResult.Fail(failure.Category, failure.Message, result.Warnings);
                else if (decoded.Value is not { IsOpen: false, Path: null, Context: null }
                    || result.ResolvedProjectPath is not null
                    || result.SessionIdentity is not { ProjectPath: null, Context: null } identity
                    || identity.WorkerSessionId != recovery.Identity.WorkerSessionId
                    || identity.PortalProcessId != recovery.Identity.PortalProcessId
                    || identity.SessionGeneration != recovery.Identity.SessionGeneration
                    || result.PortalProcessId != recovery.Identity.PortalProcessId)
                    result = WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict,
                        "The worker did not prove the same attached Portal remains empty.", result.Warnings);
            }
            if (!result.Success && expected.State == ProjectBindingSnapshot.UnboundState)
                _projectSessionBinding.TryInvalidate(expected, "The recovered empty Portal is no longer verified.");
            return result;
        });

    /// <summary>
    /// How a completed (successful) worker call changes this session's project binding. Declared
    /// explicitly per call site so the binding transition is a deliberate, readable property of
    /// each operation rather than an implicit side effect of "some call succeeded".
    /// </summary>
    private enum BindingTransition
    {
        /// <summary>No binding change. Direct status, the internal lifecycle probe, save, archive,
        /// and every unrelated data read/write use this; an unbound session stays unbound, and a
        /// bound session only gets a divergence warning if the worker reports a different project.</summary>
        None,

        /// <summary>Selection supplies its own identity validation and adoption.</summary>
        BindingNeutral,

        /// <summary>Bind the session to the worker's reported <see cref="WorkerCallResult.ResolvedProjectPath"/>.
        /// Open, create, and rebinding save-as use this; a missing resolved path is a broken
        /// postcondition, never a fallback to caller input.</summary>
        BindResolvedPath,

        /// <summary>Clear the session binding. Close uses this.</summary>
        Clear
    }

    private static bool TryValidateCompleteSessionIdentity(
        WorkerSessionIdentity? identity,
        out string? canonicalProjectPath)
    {
        canonicalProjectPath = ProjectPathNormalization.Canonicalize(identity?.ProjectPath);
        return identity is not null &&
               !string.IsNullOrWhiteSpace(identity.WorkerSessionId) &&
               identity.SessionGeneration >= 0 &&
               identity.PortalProcessId is > 0 &&
               canonicalProjectPath is not null;
    }

    private static bool SameSessionIdentity(
        WorkerSessionIdentity expected,
        WorkerSessionIdentity actual)
        => string.Equals(
               expected.WorkerSessionId,
               actual.WorkerSessionId,
               StringComparison.Ordinal) &&
           expected.SessionGeneration == actual.SessionGeneration &&
           expected.PortalProcessId == actual.PortalProcessId &&
           string.Equals(
               ProjectPathNormalization.Canonicalize(expected.ProjectPath),
               ProjectPathNormalization.Canonicalize(actual.ProjectPath),
               StringComparison.OrdinalIgnoreCase);

    private Task<T> ExecuteSerializedBindingOperationAsync<T>(Func<Task<T>> operation)
        => ExecuteSerializedBindingOperationAsync(operation, CancellationToken.None);

    private async Task<T> ExecuteSerializedBindingOperationAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ambient = _bindingOperationContext.Value;
        if (ambient is not null)
        {
            return await operation().ConfigureAwait(false);
        }

        await _bindingOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var previous = _bindingOperationContext.Value;
        _bindingOperationContext.Value = new BindingOperationContext(PinnedBinding: null);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            _bindingOperationContext.Value = previous;
            _bindingOperationGate.Release();
        }
    }

    /// <summary>
    /// Applies a call's declared <see cref="BindingTransition"/> to a SUCCESSFUL worker result.
    /// Lifecycle transitions adopt worker ground truth only after a completed successful call.
    /// Open-project selection separately validates its payload and adopts through compare-and-swap.
    /// Returns the original result, or a
    /// <c>postcondition_failed</c>/<c>binding_conflict</c> failure if a required bind could not
    /// be honored.
    /// </summary>
    private WorkerCallResult ApplyBindingTransition(
        BindingTransition transition,
        WorkerCallResult result,
        ProjectBindingSnapshot bindingBeforeCall,
        bool bindForceRebind)
    {
        switch (transition)
        {
            case BindingTransition.BindingNeutral:
                return result;
            case BindingTransition.BindResolvedPath:
                result = BindToResolvedSessionIdentity(result, bindingBeforeCall, bindForceRebind);
                if (result.Success)
                {
                    RefreshAmbientPinnedBinding();
                }

                return result;
            case BindingTransition.Clear:
                result = ValidateCloseSessionIdentity(result, bindingBeforeCall);
                if (!result.Success)
                {
                    return result;
                }

                // Close acts only on the bound project and leaves the session with nothing bound.
                _projectSessionBinding.Clear(null, out _);

                RefreshAmbientPinnedBinding();
                return result;
            case BindingTransition.None:
            default:
                return ValidateOrPromoteSessionIdentity(result, bindingBeforeCall);
        }
    }

    /// <summary>
    /// Binds the session to the worker's reported <see cref="WorkerCallResult.ResolvedProjectPath"/>.
    /// A success with no resolved path is a broken postcondition - it must NEVER fall back to the
    /// caller's requested path, a target directory/name, or anything parsed from payload text.
    /// </summary>
    private WorkerCallResult BindToResolvedSessionIdentity(
        WorkerCallResult result,
        ProjectBindingSnapshot bindingBeforeCall,
        bool forceRebind)
    {
        if (result.SessionIdentity is null)
        {
            return WorkerCallResult.Fail(
                WorkerFailureCategories.PostconditionFailed,
                "The TIA Openness worker reported success but did not return its complete session "
                + "identity, so this MCP session cannot be bound safely. Inspect the current project "
                + "state in TIA Portal before retrying.",
                result.Warnings);
        }

        var resolvedPath = ProjectPathNormalization.Canonicalize(result.ResolvedProjectPath);
        var identityPath = ProjectPathNormalization.Canonicalize(result.SessionIdentity.ProjectPath);
        if (resolvedPath is null ||
            identityPath is null ||
            !string.Equals(resolvedPath, identityPath, StringComparison.OrdinalIgnoreCase))
        {
            return WorkerCallResult.Fail(
                WorkerFailureCategories.PostconditionFailed,
                "The TIA Openness worker returned inconsistent resolvedProjectPath and sessionIdentity.projectPath values, "
                + "so this MCP session cannot be rebound safely.",
                result.Warnings);
        }

        if (bindingBeforeCall.IsVerified)
        {
            var continuityError = ValidateBindingTransitionContinuity(
                bindingBeforeCall,
                result.SessionIdentity,
                identityPath);
            if (continuityError is not null)
            {
                _projectSessionBinding.TryInvalidate(bindingBeforeCall, continuityError);
                return WorkerCallResult.Fail(
                    WorkerFailureCategories.BindingConflict,
                    continuityError,
                    result.Warnings) with
                {
                    ResolvedProjectPath = result.ResolvedProjectPath,
                    SessionIdentity = result.SessionIdentity
                };
            }
        }

        if (!_projectSessionBinding.BindVerified(result.SessionIdentity, forceRebind, out var bindError))
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict, bindError!, result.Warnings);
        }

        return result;
    }

    private WorkerCallResult ValidateCloseSessionIdentity(
        WorkerCallResult result,
        ProjectBindingSnapshot bindingBeforeCall)
    {
        if (!bindingBeforeCall.IsVerified ||
            string.IsNullOrWhiteSpace(bindingBeforeCall.WorkerSessionId) ||
            bindingBeforeCall.PortalProcessId is null ||
            bindingBeforeCall.SessionGeneration is null)
        {
            return WorkerCallResult.Fail(
                WorkerFailureCategories.BindingConflict,
                "close_project requires an existing verified worker/Portal/project binding.",
                result.Warnings);
        }

        var identity = result.SessionIdentity;
        var continuityError = identity is null
            ? "close_project succeeded without returning the worker session identity required to prove which project was closed."
            : !string.Equals(
                bindingBeforeCall.WorkerSessionId,
                identity.WorkerSessionId,
                StringComparison.Ordinal)
                ? "close_project returned a different worker session identity. The binding was not cleared."
                : bindingBeforeCall.PortalProcessId != identity.PortalProcessId
                    ? "close_project returned a different TIA Portal process identity. The binding was not cleared."
                    : identity.SessionGeneration <= bindingBeforeCall.SessionGeneration.Value
                        ? "close_project did not advance the project session generation. The binding was not cleared."
                        : ProjectPathNormalization.Canonicalize(identity.ProjectPath) is not null ||
                          ProjectPathNormalization.Canonicalize(result.ResolvedProjectPath) is not null
                            ? "close_project reported that a project is still open. The binding was not cleared."
                            : null;

        if (continuityError is null)
        {
            return result;
        }

        _projectSessionBinding.TryInvalidate(bindingBeforeCall, continuityError);
        return WorkerCallResult.Fail(
            WorkerFailureCategories.BindingConflict,
            continuityError,
            result.Warnings) with
        {
            ResolvedProjectPath = result.ResolvedProjectPath,
            SessionIdentity = result.SessionIdentity
        };
    }

    private static string? ValidateBindingTransitionContinuity(
        ProjectBindingSnapshot bindingBeforeCall,
        WorkerSessionIdentity identity,
        string canonicalNewPath)
    {
        if (!string.Equals(
                bindingBeforeCall.WorkerSessionId,
                identity.WorkerSessionId,
                StringComparison.Ordinal))
        {
            return "The lifecycle response came from a different worker session. The new project identity was not adopted.";
        }

        if (bindingBeforeCall.PortalProcessId != identity.PortalProcessId)
        {
            return "The lifecycle response came from a different TIA Portal process. The new project identity was not adopted.";
        }

        if (bindingBeforeCall.SessionGeneration is null ||
            string.IsNullOrWhiteSpace(bindingBeforeCall.ProjectPath))
        {
            return "The prior verified binding was incomplete, so lifecycle identity continuity could not be proven.";
        }

        var samePath = string.Equals(
            ProjectPathNormalization.Canonicalize(bindingBeforeCall.ProjectPath),
            canonicalNewPath,
            StringComparison.OrdinalIgnoreCase);
        if (samePath && identity.SessionGeneration != bindingBeforeCall.SessionGeneration.Value)
        {
            return "The lifecycle response reused the same project path with a different generation, which indicates a close/reopen identity change.";
        }

        if (!samePath && identity.SessionGeneration <= bindingBeforeCall.SessionGeneration.Value)
        {
            return "The lifecycle response changed project path without advancing the session generation.";
        }

        return null;
    }

    private void RefreshAmbientPinnedBinding()
    {
        var context = _bindingOperationContext.Value;
        if (context is not null)
        {
            context.PinnedBinding = _projectSessionBinding.CaptureSnapshot();
        }
    }

    /// <summary>
    /// Verifies worker identity for an existing binding, or promotes an explicitly configured
    /// startup path after the first matching worker response. Only status, selection and
    /// inventory calls run unbound; content reads fail closed until a project is bound. Divergence is a hard failure: a warning after a write would be too late.
    /// </summary>
    private WorkerCallResult ValidateOrPromoteSessionIdentity(
        WorkerCallResult result,
        ProjectBindingSnapshot bindingBeforeCall)
    {
        if (string.Equals(bindingBeforeCall.State, ProjectBindingSnapshot.UnboundState, StringComparison.Ordinal))
        {
            return result;
        }

        if (string.Equals(
                bindingBeforeCall.State,
                ProjectBindingSnapshot.ConfiguredUnverifiedState,
                StringComparison.Ordinal))
        {
            if (_projectSessionBinding.TryPromoteConfigured(result.SessionIdentity, out var promoteError))
            {
                return result;
            }

            _projectSessionBinding.TryInvalidate(
                bindingBeforeCall,
                promoteError ?? "worker identity could not verify the configured project");
            return WorkerCallResult.Fail(
                result.SessionIdentity is null
                    ? WorkerFailureCategories.PostconditionFailed
                    : WorkerFailureCategories.BindingConflict,
                promoteError ?? "The worker did not return a verifiable project identity.",
                result.Warnings) with
            {
                IsPostOperationFailure = true,
                SessionIdentity = result.SessionIdentity,
                ConfigureNetworkDeviceOutcome = result.ConfigureNetworkDeviceOutcome,
                HmiWriteOutcome = result.HmiWriteOutcome,
                VciWriteOutcome = result.VciWriteOutcome,
                BlockImportOutcome = result.BlockImportOutcome,
                DispatchState = result.DispatchState
            };
        }

        if (string.Equals(bindingBeforeCall.State, ProjectBindingSnapshot.VerifiedState, StringComparison.Ordinal))
        {
            var currentBinding = _projectSessionBinding.CaptureSnapshot();
            if (!bindingBeforeCall.SameBinding(currentBinding))
            {
                return WorkerCallResult.Fail(
                    WorkerFailureCategories.BindingConflict,
                    "The MCP project binding changed while this worker request was in flight. "
                    + "The response was discarded and the newer binding was left intact.",
                    result.Warnings) with
                {
                    IsPostOperationFailure = true,
                    SessionIdentity = result.SessionIdentity,
                    ConfigureNetworkDeviceOutcome = result.ConfigureNetworkDeviceOutcome,
                    HmiWriteOutcome = result.HmiWriteOutcome,
                    VciWriteOutcome = result.VciWriteOutcome,
                    BlockImportOutcome = result.BlockImportOutcome,
                    DispatchState = result.DispatchState
                };
            }

            if (_projectSessionBinding.MatchesVerifiedIdentity(result.SessionIdentity, out var identityError))
            {
                return result;
            }

            _projectSessionBinding.TryInvalidate(
                bindingBeforeCall,
                identityError ?? "worker identity changed");
            _logger?.LogError("TIA Openness worker binding divergence: {Error}", identityError);
            return WorkerCallResult.Fail(
                result.SessionIdentity is null
                    ? WorkerFailureCategories.PostconditionFailed
                    : WorkerFailureCategories.BindingConflict,
                identityError ?? "The worker session identity changed.",
                result.Warnings) with
            {
                IsPostOperationFailure = true,
                SessionIdentity = result.SessionIdentity,
                ConfigureNetworkDeviceOutcome = result.ConfigureNetworkDeviceOutcome,
                HmiWriteOutcome = result.HmiWriteOutcome,
                VciWriteOutcome = result.VciWriteOutcome,
                BlockImportOutcome = result.BlockImportOutcome,
                DispatchState = result.DispatchState
            };
        }

        return WorkerCallResult.Fail(
            WorkerFailureCategories.BindingConflict,
            "The MCP project binding is invalidated. Rebind explicitly before continuing.",
            result.Warnings) with
        {
            SessionIdentity = result.SessionIdentity,
            ConfigureNetworkDeviceOutcome = result.ConfigureNetworkDeviceOutcome,
            HmiWriteOutcome = result.HmiWriteOutcome,
            VciWriteOutcome = result.VciWriteOutcome,
            BlockImportOutcome = result.BlockImportOutcome,
            DispatchState = result.DispatchState
        };
    }

    private void InvalidateVerifiedBinding(string reason)
    {
        if (_projectSessionBinding.IsVerified)
        {
            _projectSessionBinding.Invalidate(reason);
        }
    }

    internal void InvalidateRejectedLifecycleContext()
        => InvalidateVerifiedBinding("the lifecycle verification returned contradictory owner evidence");

    private sealed class BindingOperationContext
    {
        public BindingOperationContext(ProjectBindingSnapshot? PinnedBinding)
        {
            this.PinnedBinding = PinnedBinding;
        }

        public ProjectBindingSnapshot? PinnedBinding { get; set; }
    }

    public sealed record PinnedBindingExecutionResult<T>(
        bool Success,
        T? Value,
        WorkerCallResult? Failure)
        where T : class
    {
        public static PinnedBindingExecutionResult<T> Ok(T value)
            => new(true, value, Failure: null);

        public static PinnedBindingExecutionResult<T> Fail(WorkerCallResult failure)
            => new(false, Value: null, failure);
    }
}

public sealed record ProjectInspectionOutcome(ProjectBindingSnapshot Before, ProjectBindingSnapshot After,
    WorkerCallResult Result, IReadOnlyList<TiaPortalProcessInfo> Portals);
