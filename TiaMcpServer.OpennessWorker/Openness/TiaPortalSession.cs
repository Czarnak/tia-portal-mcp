using Siemens.Engineering;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public class TiaPortalSession : IDisposable
{
    private readonly bool _allowTiaConfirmations;
    private readonly string _workerSessionId = Guid.NewGuid().ToString("N");
    private TiaPortal? _tiaPortal;
    private TiaPortal? _unreleasedTemporaryTarget;
    private ActiveProjectContext? _activeContext;
    private bool _disposed;
    private int? _attachedProcessId;
    private string? _selectedProjectPath;
    private long _sessionGeneration;
    private long _portalAttachmentRevision;

    public TiaPortalSession(bool allowTiaConfirmations = false)
    {
        // Even when a caller requests automatic confirmations, an explicitly configured
        // read-only worker must reject every TIA confirmation dialog. This keeps the final
        // Siemens-facing layer fail-closed if a nominally read operation unexpectedly asks
        // TIA Portal to confirm a state-changing action.
        var accessMode = WorkerOperationAuthorization.ParseAccessMode(Environment.GetCommandLineArgs());
        _allowTiaConfirmations = allowTiaConfirmations &&
            WorkerOperationAuthorization.AllowsTiaConfirmations(accessMode);
    }

    internal ActiveProjectContext? ActiveContext => _activeContext;
    internal ProjectBase? EngineeringRoot => _activeContext?.EngineeringRoot;

    /// <summary>Standalone-only compatibility bridge for existing content services.</summary>
    public Project? Project => (_activeContext?.Owner as StandaloneProjectOwner)?.Project;

    internal StandaloneProjectOwner RequireStandaloneOwner()
    {
        if (_activeContext is null)
            throw new InvalidOperationException(ProjectOpenPolicy.NoProjectOpenMessage(McpAccessMode.ReadWrite));
        if (_activeContext.Owner is StandaloneProjectOwner owner)
            return owner;
        throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            "This operation requires a standalone project. The active local session must use its explicit lifecycle operations.");
    }

    public TiaPortal? TiaPortal => _tiaPortal;

    public bool IsConnected => _tiaPortal != null;

    public int? CurrentProcessId => _attachedProcessId;
    internal long PortalAttachmentRevision => _portalAttachmentRevision;

    public WorkerSessionIdentity GetSessionIdentity()
    {
        // Identity describes the live Siemens handle, not the sticky path retained for a later
        // explicit re-scan. If the handle was closed in the UI, TryReadCurrentProjectPath clears
        // it and advances the generation; returning the retained path here would falsely certify
        // a project that is no longer open.
        var liveProjectPath = ProjectPathNormalization.Canonicalize(TryReadCurrentProjectPath());
        return new WorkerSessionIdentity
        {
            WorkerSessionId = _workerSessionId,
            SessionGeneration = Interlocked.Read(ref _sessionGeneration),
            PortalProcessId = _attachedProcessId,
            ProjectPath = liveProjectPath,
            Context = _activeContext?.Owner is LocalSessionOwner ? _activeContext.ToInfo() : null
        };
    }

    public void ValidateExpectedSessionIdentity(
        WorkerSessionIdentity? expected,
        bool allowMissingExpectedIdentity,
        bool useCachedIdentity = false)
    {
        if (expected is null)
        {
            if (allowMissingExpectedIdentity)
            {
                return;
            }

            throw new WorkerOperationException(
                WorkerFailureCategories.BindingConflict,
                "This operation requires a verified project binding. Use bind_project first, or "
                + "configure --project and let the host verify it before this operation. In read-write/full mode, "
                + "you can also use open_project or create_project.");
        }

        var current = useCachedIdentity ? GetCachedSessionIdentity() : GetSessionIdentity();
        if (string.Equals(expected.WorkerSessionId, current.WorkerSessionId, StringComparison.Ordinal)
            && expected.SessionGeneration == current.SessionGeneration
            && expected.PortalProcessId == current.PortalProcessId
            && PathsEqual(expected.ProjectPath, current.ProjectPath))
        {
            return;
        }

        throw new WorkerOperationException(
            WorkerFailureCategories.BindingConflict,
            "The expected Worker/TIA/project session identity no longer matches the live worker session. "
            + $"Expected worker='{expected.WorkerSessionId}', generation={expected.SessionGeneration}, "
            + $"PID={FormatProcessId(expected.PortalProcessId)}, project='{expected.ProjectPath ?? "(none)"}'; "
            + $"current worker='{current.WorkerSessionId}', generation={current.SessionGeneration}, "
            + $"PID={FormatProcessId(current.PortalProcessId)}, project='{current.ProjectPath ?? "(none)"}'. "
            + "Refresh project status and obtain a new explicitly verified binding before retrying.");
    }

    public void Connect(string? requestedProjectPath, int? requestedProcessId = null)
    {
        ThrowIfDisposed();
        if (requestedProcessId.HasValue && requestedProcessId <= 0)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                "PortalProcessId must be positive.");

        if (IsConnected)
        {
            if (requestedProcessId.HasValue && requestedProcessId != _attachedProcessId)
                throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                    "The requested Portal PID differs from the attached Portal. Use explicit selection to switch.");
            return;
        }

        if (requestedProcessId.HasValue)
        {
            EnsurePortalConnected(requestedProcessId);
            SelectOpenProject(ProjectPathNormalization.Canonicalize(requestedProjectPath));
            return;
        }

        var inventory = TiaPortalProcessInventory.Read();
        var candidates = inventory.Select(entry => entry.Candidate).ToList();
        var selectedProcessId = TiaPortalTargetSelector.SelectProcessId(candidates, requestedProjectPath);
        var entry = inventory.FirstOrDefault(item => item.Candidate.Id == selectedProcessId);
        var selectedProcess = entry?.Process;

        if (selectedProcess is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.PostconditionFailed,
                $"TIA Portal selector chose PID {selectedProcessId}, but that process was no longer available for Attach().");
        }

        AttachPortal(selectedProcess, selectedProcessId);
        // Projects present when we attach belong to the TIA Portal UI, never this worker.
        SelectOpenProject(ProjectPathNormalization.Canonicalize(requestedProjectPath));

        Console.Error.WriteLine(
            $"Connected to TIA Portal PID {_attachedProcessId}"
            + $" with project '{CurrentProjectPath ?? "(none)"}'.");
    }

    private void AttachPortal(TiaPortalProcess selectedProcess, int selectedProcessId)
    {
        ReleaseTemporaryTargetIfSafe();
        var attachedPortal = selectedProcess.Attach();
        int attachedProcessId;
        try { attachedProcessId = attachedPortal.GetCurrentProcess().Id; }
        catch (Exception)
        {
            _unreleasedTemporaryTarget = attachedPortal;
            ReleaseTemporaryTargetIfSafe();
            throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                "TIA Portal Attach() returned a handle whose PID could not be verified. No project was selected.");
        }
        if (attachedProcessId != selectedProcessId)
        {
            _unreleasedTemporaryTarget = attachedPortal;
            ReleaseTemporaryTargetIfSafe();
            throw new WorkerOperationException(
                WorkerFailureCategories.BindingConflict,
                $"TIA Portal Attach() targeted PID {selectedProcessId} but returned PID {attachedProcessId}. No operation was performed.");
        }

        SetPortalHandle(attachedPortal, attachedProcessId);
        attachedPortal.Notification += OnNotification;
        attachedPortal.Confirmation += OnConfirmation;
        attachedPortal.Disposed += OnDisposed;
    }

    /// <summary>Validates or establishes only the persistent Portal attachment; never selects a project.</summary>
    public void EnsurePortalConnected(int? requestedProcessId = null)
    {
        ThrowIfDisposed();
        if (requestedProcessId.HasValue && requestedProcessId.Value <= 0)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "PortalProcessId must be positive.");

        if (IsConnected)
        {
            // A deliberate foreign-PID refusal precedes even the live handle check.
            if (requestedProcessId.HasValue && requestedProcessId != _attachedProcessId)
                throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                    "Inventory is limited to the currently attached Portal. Use explicit binding to select another instance.");
            ValidatePortalProcess();
            return;
        }

        var inventory = TiaPortalProcessInventory.Read();
        var matches = inventory.Where(entry => !requestedProcessId.HasValue || entry.Candidate.Id == requestedProcessId).ToList();
        if (matches.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, "No matching running TIA Portal instance was found.");
        if (matches.Count != 1)
            throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, "Multiple running TIA Portal instances require an exact PortalProcessId.");
        AttachPortal(matches[0].Process, matches[0].Candidate.Id);
    }

    private void ValidatePortalProcess()
    {
        int actualProcessId;
        try { actualProcessId = _tiaPortal!.GetCurrentProcess().Id; }
        catch (Exception)
        {
            // Forget dead evidence without attaching, detaching, or disposing any project/session.
            OnDisposed(_tiaPortal, EventArgs.Empty);
            throw new WorkerOperationException(WorkerFailureCategories.BindingConflict, "The attached TIA Portal instance is no longer verifiable.");
        }
        if (_attachedProcessId != actualProcessId)
        {
            var expectedProcessId = _attachedProcessId;
            SetPortalHandle(_tiaPortal, actualProcessId);
            throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                $"The attached TIA Portal PID changed from {FormatProcessId(expectedProcessId)} to {actualProcessId}. No operation was performed.");
        }
    }

    private WorkerSessionIdentity GetCachedSessionIdentity()
        => new()
        {
            WorkerSessionId = _workerSessionId,
            SessionGeneration = Interlocked.Read(ref _sessionGeneration),
            PortalProcessId = _attachedProcessId,
            ProjectPath = _selectedProjectPath,
            Context = _activeContext?.Owner is LocalSessionOwner ? _activeContext.ToInfo() : null
        };

    public PortalProjectSelectionInfo SelectPortalProject(string projectPath)
        => SelectPortalProject(projectPath, null);

    public PortalProjectSelectionInfo SelectPortalProject(string projectPath, int? requestedProcessId)
    {
        ThrowIfDisposed();
        ReleaseTemporaryTargetIfSafe();

        // Lookup precedes every session transition, including the first attach and same-PID selection.
        var inventory = TiaPortalProcessInventory.Read();
        if (requestedProcessId.HasValue && requestedProcessId <= 0)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                "PortalProcessId must be positive.");
        var selectedProcessId = requestedProcessId
            ?? TiaPortalTargetSelector.SelectExactProcessId(
                inventory.Select(entry => entry.Candidate).ToList(), projectPath);
        if (!inventory.Any(entry => entry.Candidate.Id == selectedProcessId))
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                "No running TIA Portal instance has the requested PortalProcessId.");
        var selectedProcess = inventory.First(entry => entry.Candidate.Id == selectedProcessId).Process;
        var previous = new PortalProjectSelectionInfo
        {
            PreviousProcessId = _attachedProcessId,
            PreviousProjectPath = _selectedProjectPath,
            PreviousProjectWasWorkerOpened = _activeContext?.Owner.OpenedByWorker == true,
            PreviousProjectIsModified = TryReadProjectIsModified()
        };

        if (IsConnected && _attachedProcessId == selectedProcessId)
        {
            var projects = ReadOpenContexts();
            var selectedIndex = TiaPortalTargetSelector.SelectProjectIndex(
                projects.Select(candidate => (string?)candidate.BindingPath).ToList(), projectPath);
            if (selectedIndex is null)
            {
                throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                    $"Requested project '{projectPath}' is no longer open in TIA Portal PID {selectedProcessId}. No project was selected.");
            }

            AdoptContext(projects[selectedIndex.Value].Context, projectPath);
            return previous;
        }

        // A temporary target attachment must itself have a known retaining client. Otherwise a
        // missing/ambiguous owner could leave us unable to release the last headless local client.
        if (!HasRetainingClient(selectedProcess))
            throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                "The target Portal has no verified retaining client for a temporary attachment.");

        TiaPortal? target = null;
        var promoted = false;
        try
        {
            target = selectedProcess.Attach();
            var actualProcessId = target.GetCurrentProcess().Id;
            if (actualProcessId != selectedProcessId)
                throw new InvalidOperationException($"TIA Portal Attach() targeted PID {selectedProcessId} but returned PID {actualProcessId}.");

            var projects = ReadOpenContexts(target);
            var selectedIndex = TiaPortalTargetSelector.SelectProjectIndex(
                projects.Select(candidate => (string?)candidate.BindingPath).ToList(), projectPath);
            if (selectedIndex is null)
                throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                    $"Requested project '{projectPath}' is no longer open in TIA Portal PID {selectedProcessId}.");

            var selectedOwner = projects[selectedIndex.Value].Context;
            var verified = ReadOpenContexts(target);
            var verifiedIndex = TiaPortalTargetSelector.SelectProjectIndex(
                verified.Select(candidate => (string?)candidate.BindingPath).ToList(), projectPath);
            if (verifiedIndex is null)
                throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                    "The target owner disappeared before source release.");
            var verifiedOwner = verified[verifiedIndex.Value].Context;
            if (selectedOwner.Owner.GetType() != verifiedOwner.Owner.GetType()
                || !object.Equals(selectedOwner.EngineeringRoot, verifiedOwner.EngineeringRoot)
                || selectedOwner.Owner is LocalSessionOwner firstLocal
                && verifiedOwner.Owner is LocalSessionOwner secondLocal
                && !object.Equals(firstLocal.LocalSession, secondLocal.LocalSession))
                throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                    "The target owner changed before source release.");

            if (IsConnected)
                Disconnect();

            SetPortalHandle(target, actualProcessId);
            target.Notification += OnNotification;
            target.Confirmation += OnConfirmation;
            target.Disposed += OnDisposed;
            promoted = true;

            AdoptContext(verifiedOwner, projectPath);
            previous.Reattached = true;
            return previous;
        }
        catch (Exception ex)
        {
            if (!promoted && target is not null)
            {
                var refusal = EvaluatePortalDetach(target);
                if (refusal is not null)
                {
                    _unreleasedTemporaryTarget = target;
                    throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                        "The temporary target Portal cannot be safely released: " + refusal);
                }
                target.Dispose();
            }
            if (ex is WorkerOperationException operation)
                throw operation;
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                $"Could not select the requested Portal project: {ex.Message}");
        }
    }

    private bool? TryReadProjectIsModified()
        => TryReadProjectIsModified(EngineeringRoot);

    private static bool? TryReadProjectIsModified(ProjectBase? project)
    {
        try { return project?.IsModified; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read project modified state: {ex.Message}");
            return null;
        }
    }

    private static IReadOnlyList<bool?>? ReadAttachedProjectModifiedStates(TiaPortal portal)
    {
        try { return portal.Projects.Select(TryReadProjectIsModified).ToList(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read attached Portal projects: {ex.Message}");
            return null;
        }
    }

    public void OpenProject(string projectPath)
    {
        ThrowIfDisposed();
        if (_activeContext?.ContainerKind == ProjectContainerKinds.ServerProject)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                "A server-project owner cannot be replaced through open_project.");
        if (string.IsNullOrWhiteSpace(projectPath) || !Path.IsPathRooted(projectPath))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                "ProjectPath must be an absolute .ap21 or .als21 file path.");
        var extension = Path.GetExtension(projectPath);
        var isLocal = string.Equals(extension, ".als21", StringComparison.OrdinalIgnoreCase);
        if (!isLocal && !string.Equals(extension, ".ap21", StringComparison.OrdinalIgnoreCase))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                "Only .ap21 and .als21 files can be opened.");
        if (!File.Exists(projectPath))
            throw new FileNotFoundException("TIA Portal project file was not found.", projectPath);
        var requestedPath = Path.GetFullPath(projectPath);

        if (!IsConnected)
            Connect(requestedPath);

        var sourceContext = _activeContext;
        var currentPath = TryReadCurrentProjectPath();
        if (sourceContext is not null && _activeContext is null)
            throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                "The selected source owner changed before the open. No opener was called.");

        var openContexts = ReadOpenContexts();
        if (isLocal)
        {
            if (_activeContext?.Owner is LocalSessionOwner existingLocal
                && PathsEqual(_activeContext.SessionContainerPath, requestedPath))
            {
                // The exact input belongs to the continuously verified selected owner.
                if (openContexts.Count(candidate => candidate.Context.Owner is LocalSessionOwner owner
                    && object.Equals(owner.LocalSession, existingLocal.LocalSession)
                    && object.Equals(owner.Project, existingLocal.Project)) != 1)
                    throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                        "The previously opened local-session owner is no longer unique.");
                return;
            }

            if (_activeContext?.Owner is LocalSessionOwner)
                throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                    "local_session_requires_terminal_operation: another session cannot be opened while a local owner is selected.");

            // An unrelated owner could be this ALS destination; the installed API exposes no
            // reverse owner-to-ALS join. Empty Portal is the only qualified opening case.
            var currentStandalone = _activeContext?.Owner as StandaloneProjectOwner;
            if (openContexts.Any(candidate => currentStandalone is null
                || !object.Equals(candidate.Context.EngineeringRoot, currentStandalone.Project)))
                throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                    "An already-open owner prevents proving that this ALS destination is unopened.");
            if (currentStandalone is not null && !currentStandalone.OpenedByWorker)
                throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                    "Preservation of the user-opened source during LocalSessions.Open is unproved.");

            if (currentStandalone is not null)
            {
                if (openContexts.Count != 1
                    || !object.Equals(openContexts[0].Context.EngineeringRoot, currentStandalone.Project))
                    throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                        "The selected standalone source is no longer the sole open owner.");
                var sourceIdentity = GetSessionIdentity();
                ValidateExpectedSessionIdentity(sourceIdentity, false);
                var beforeClose = ReadOpenContexts();
                if (beforeClose.Count != 1
                    || !object.Equals(beforeClose[0].Context.EngineeringRoot, currentStandalone.Project))
                    throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                        "The selected standalone source changed before close.");
                ProjectRebindCloseGuard.CloseBeforeRebind(
                    () => currentStandalone.Project.IsModified,
                    () => currentStandalone.Project.Close(),
                    () => SetActiveContext(null));
            }

            if (ReadOpenContexts().Count != 0)
                throw new WorkerOperationException(currentStandalone is null
                    ? WorkerFailureCategories.GuardBlocked : WorkerFailureCategories.PostconditionFailed,
                    currentStandalone is null
                        ? "The Portal gained an owner before LocalSessions.Open; no opener was called."
                        : "The Portal is not empty after source close. Inspect the possible mutation before retrying.");

            var openedOwner = _tiaPortal!.LocalSessions.Open(new FileInfo(requestedPath));
            var openedContext = LocalSessionContextResolver.Resolve(openedOwner, true);
            var afterOpen = ReadOpenContexts();
            if (afterOpen.Count != 1 || afterOpen[0].Context.Owner is not LocalSessionOwner liveOwner
                || !object.Equals(liveOwner.LocalSession, openedOwner)
                || !object.Equals(liveOwner.Project, openedContext.EngineeringRoot)
                || !PathsEqual(afterOpen[0].BindingPath, openedContext.BindingPath))
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                    "LocalSessions.Open returned an owner that could not be uniquely verified. The open may have succeeded; inspect before retrying.");
            openedContext.RecordSuccessfulOpen(requestedPath);
            AdoptContext(openedContext, openedContext.BindingPath);
            return;
        }

        if (_activeContext?.Owner is LocalSessionOwner)
            throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                "local_session_requires_terminal_operation: another project cannot be opened while a local owner is selected.");

        if (currentPath is not null && PathsEqual(currentPath, requestedPath))
            return;

        var alreadyOpen = openContexts.Where(candidate => PathsEqual(candidate.BindingPath, requestedPath)).ToList();
        if (alreadyOpen.Count > 1)
            throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                "Multiple already-open projects have the requested path.");
        if (alreadyOpen.Count == 1)
        {
            if (_activeContext?.Owner.OpenedByWorker == true)
                throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                    "Cannot abandon worker ownership of another open project during destination reuse.");
            AdoptContext(alreadyOpen[0].Context, requestedPath);
            return;
        }

        if (openContexts.Any(candidate => candidate.Context.Owner is LocalSessionOwner))
            throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                "Preservation of an already-open local session during Projects.Open is unproved.");

        if (Project is not null)
        {
            if (_activeContext?.Owner.OpenedByWorker == true)
            {
                var currentProject = RequireStandaloneOwner().Project;
                try
                {
                    ProjectRebindCloseGuard.CloseBeforeRebind(
                        () => currentProject.IsModified,
                        () =>
                        {
                            Console.Error.WriteLine($"Closing project '{currentPath ?? "(unknown)"}' before opening '{requestedPath}'.");
                            currentProject.Close();
                        },
                        () =>
                        {
                            SetActiveContext(null);
                            var openedProject = _tiaPortal!.Projects.Open(new FileInfo(requestedPath));
                            AdoptProject(openedProject, openedByWorker: true, requestedPath);
                        });
                    return;
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine($"Could not close the previous project: {ex.Message}");
                    throw;
                }
            }
            else
            {
                // The user opened this project in the TIA Portal UI; it is not ours to close.
                Console.Error.WriteLine($"Leaving user-opened project '{currentPath ?? "(unknown)"}' open; opening '{requestedPath}' alongside it.");
            }

        }

        var project = _tiaPortal!.Projects.Open(new FileInfo(requestedPath));
        AdoptProject(project, openedByWorker: true, requestedPath);
    }

    /// <summary>Absolute path of the attached project, or null when nothing is attached.</summary>
    public string? CurrentProjectPath => TryReadCurrentProjectPath();

    /// <summary>Reads the selected project's rebind state without opening or closing a project.</summary>
    internal ProjectRebindStateInfo ReadProjectRebindState(string destinationProjectPath)
    {
        ThrowIfDisposed();
        if (_activeContext?.ContainerKind == ProjectContainerKinds.ServerProject)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                "A server-project owner cannot be used for this rebind probe.");

        var destination = ProjectPathNormalization.Canonicalize(destinationProjectPath)
            ?? throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "A destination project path is required for the rebind-state probe.");
        var source = ProjectPathNormalization.Canonicalize(TryReadCurrentProjectPath());
        if (source is null)
        {
            return ProjectRebindStateInfo.Create(null, destination, null, sourceOpenedByWorker: false);
        }

        var currentProject = EngineeringRoot;
        if (currentProject is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                "The selected source project became unavailable during the rebind-state probe.");
        }

        bool isModified;
        try
        {
            isModified = currentProject.IsModified;
        }
        catch (Exception)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                "Could not verify whether the selected source project has unsaved changes. "
                + "No project was opened or closed.");
        }

        return ProjectRebindStateInfo.Create(source, destination, isModified,
            _activeContext?.Owner.OpenedByWorker == true,
            _activeContext?.Owner is LocalSessionOwner ? _activeContext.ToInfo() : null);
    }

    internal void TrackWorkerOpenedProject(Project project)
        => AdoptProject(project, openedByWorker: true, expectedProjectPath: null);

    /// <summary>
    /// Accepts an authorized in-place path transition such as Siemens SaveAs. The project handle
    /// stays the same, so the generation must be advanced explicitly when its identity changes.
    /// </summary>
    internal void AcceptCurrentProjectIdentity()
    {
        var currentPath = ProjectPathNormalization.Canonicalize(TryReadCurrentProjectPath());
        if (currentPath is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.PostconditionFailed,
                "TIA Portal did not expose a project path after the authorized project transition.");
        }

        if (!PathsEqual(_selectedProjectPath, currentPath))
        {
            _selectedProjectPath = currentPath;
            IncrementGeneration();
        }
    }

    private string? TryReadCurrentProjectPath()
    {
        if (EngineeringRoot is null)
        {
            return null;
        }

        if (_activeContext?.Owner is LocalSessionOwner localOwner)
        {
            try
            {
                var matching = ReadOpenContexts().Where(candidate =>
                    candidate.Context.Owner is LocalSessionOwner
                    && PathsEqual(candidate.BindingPath, _selectedProjectPath)).ToList();
                if (matching.Count != 1
                    || matching[0].Context.Owner is not LocalSessionOwner liveOwner
                    || !object.Equals(localOwner.LocalSession, liveOwner.LocalSession)
                    || !object.Equals(localOwner.Project, liveOwner.Project))
                {
                    SetActiveContext(null);
                    return null;
                }
            }
            catch (WorkerOperationException)
            {
                SetActiveContext(null);
                throw;
            }
        }

        try
        {
            var currentPath = EngineeringRoot.Path?.FullName;
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                return currentPath;
            }

            // A live project used for a bound engineering operation must always have a readable
            // identity. Treat a null/blank path exactly like a stale handle: clearing the handle
            // advances the generation, so the post-refresh ExpectedSessionIdentity check fails
            // before the operation body can touch an unidentified project.
            SetActiveContext(null);
            return null;
        }
        catch (EngineeringException)
        {
            // Stale handle: the project was closed in the TIA Portal UI since we opened it.
            SetActiveContext(null);
            return null;
        }
    }

    internal void MarkProjectClosed()
    {
        var hadProjectHandle = _activeContext is not null;
        var hadSelectedProject = _selectedProjectPath is not null;
        SetActiveContext(null);
        _selectedProjectPath = null;
        if (hadSelectedProject && !hadProjectHandle)
        {
            // SetActiveContext already advanced the generation when a live handle was cleared.
            // If the handle had already gone stale, clearing the retained identity is itself the
            // observable session transition that invalidates earlier safety evidence.
            IncrementGeneration();
        }
    }

    public void EnsureConnected(string? requestedProjectPath, int? requestedProcessId = null)
    {
        ThrowIfDisposed();
        if (requestedProcessId.HasValue && requestedProcessId <= 0)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                "PortalProcessId must be positive.");

        if (!IsConnected)
        {
            Connect(requestedProjectPath, requestedProcessId);
            return;
        }

        if (requestedProcessId.HasValue && requestedProcessId != _attachedProcessId)
            throw new WorkerOperationException(WorkerFailureCategories.BindingConflict,
                "The requested Portal PID differs from the attached Portal. Use explicit selection to switch.");

        ValidatePortalProcess();

        if (_activeContext is not null)
        {
            // The bound project may have been closed in the TIA Portal UI since we last read
            // it. Detect that now, in the same call, instead of leaving it for whichever call
            // happens to touch Project.Path next (e.g. CurrentProjectPath) — otherwise a caller
            // sees one stale response (nothing bound) before a *second* request finally
            // rescans and picks up whatever project is open now. Openness has no "project
            // closed" event to push this proactively, so detect-then-rescan has to happen
            // within a single EnsureConnected() call.
            var actualPath = ProjectPathNormalization.Canonicalize(TryReadCurrentProjectPath());
            if (actualPath is not null && _selectedProjectPath is not null
                && !PathsEqual(actualPath, _selectedProjectPath))
            {
                var previousPath = _selectedProjectPath;
                _selectedProjectPath = actualPath;
                IncrementGeneration();
                throw new WorkerOperationException(
                    WorkerFailureCategories.BindingConflict,
                    $"The selected TIA project changed outside this worker from '{previousPath}' "
                    + $"to '{actualPath}'. No operation was performed.");
            }
        }

        if (_activeContext is null)
        {
            // Nothing bound — either there never was a project, or the probe above just found
            // the previous handle stale. A project may have been opened in the TIA Portal UI —
            // re-scan instead of requiring a worker restart to pick it up. The exact requested
            // or retained path is authoritative; a different sole project is never adopted as a
            // fallback after a previously selected project disappears.
            SelectOpenProject(
                ProjectPathNormalization.Canonicalize(requestedProjectPath) ?? _selectedProjectPath);
        }
    }

    private void SelectOpenProject(string? expectedProjectPath)
    {
        if (_tiaPortal is null)
        {
            return;
        }

        var candidates = ReadOpenContexts();
        var selectedIndex = TiaPortalTargetSelector.SelectProjectIndex(
            candidates.Select(candidate => (string?)candidate.BindingPath).ToList(), expectedProjectPath);
        if (selectedIndex is null)
        {
            SetActiveContext(null);
            return;
        }

        AdoptContext(candidates[selectedIndex.Value].Context, expectedProjectPath);
    }

    private static bool ReadAttachedLocalSessionPresence(TiaPortal portal)
    {
        try { return portal.LocalSessions.Any(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read attached Portal local sessions: {ex.Message}");
            return true;
        }
    }

    private static bool HasRetainingClient(TiaPortalProcess process)
    {
        try
        {
            return TiaPortalProcessInventory.TryReadHasUserInterface(process)
                || process.AttachedSessions.Any(session =>
                    session.ProcessId != System.Diagnostics.Process.GetCurrentProcess().Id);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? EvaluatePortalDetach(TiaPortal portal)
    {
        var hasUserInterface = false;
        var otherClientCount = 0;
        try
        {
            var process = portal.GetCurrentProcess();
            hasUserInterface = TiaPortalProcessInventory.TryReadHasUserInterface(process);
            var workerProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
            otherClientCount = process.AttachedSessions.Count(session => session.ProcessId != workerProcessId);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read attached Portal clients: {ex.Message}");
        }

        return PortalDetachGuard.EvaluateProjects(hasUserInterface, otherClientCount,
            ReadAttachedProjectModifiedStates(portal), ReadAttachedLocalSessionPresence(portal));
    }

    private void ReleaseTemporaryTargetIfSafe()
    {
        if (_unreleasedTemporaryTarget is null)
            return;
        var refusal = EvaluatePortalDetach(_unreleasedTemporaryTarget);
        if (refusal is not null)
            throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked,
                "The retained temporary target Portal cannot be safely released: " + refusal);
        _unreleasedTemporaryTarget.Dispose();
        _unreleasedTemporaryTarget = null;
    }

    internal IReadOnlyList<OpenProjectContextCandidate> ReadOpenContexts()
    {
        if (_tiaPortal is null)
            return Array.Empty<OpenProjectContextCandidate>();

        return ReadOpenContexts(_tiaPortal);
    }

    private static IReadOnlyList<OpenProjectContextCandidate> ReadOpenContexts(TiaPortal portal)
    {
        var contexts = new List<OpenProjectContextCandidate>();
        foreach (var project in portal.Projects)
            contexts.Add(new OpenProjectContextCandidate(ActiveProjectContext.ForStandalone(project, false)));
        foreach (var owner in portal.LocalSessions)
            contexts.Add(new OpenProjectContextCandidate(LocalSessionContextResolver.Resolve(owner, false)));
        return contexts;
    }

    private void AdoptProject(Project project, bool openedByWorker, string? expectedProjectPath)
        => AdoptContext(ActiveProjectContext.ForStandalone(project, openedByWorker), expectedProjectPath);

    internal void AdoptContext(ActiveProjectContext context, string? expectedProjectPath)
    {
        ThrowIfDisposed();
        if (context is null) throw new ArgumentNullException(nameof(context));
        var actualPath = ProjectPathNormalization.Canonicalize(TryReadProjectPathForSelection(context.EngineeringRoot));
        if (actualPath is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.PostconditionFailed,
                "TIA Portal returned a project handle without a readable absolute path. No project was selected.");
        }

        var expectedPath = ProjectPathNormalization.Canonicalize(expectedProjectPath);
        if (expectedPath is not null && !PathsEqual(expectedPath, actualPath))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.PostconditionFailed,
                $"TIA Portal returned project '{actualPath}' while '{expectedPath}' was required. "
                + "The returned handle was not selected; inspect the TIA Portal UI before retrying.");
        }

        if (_activeContext?.Owner is LocalSessionOwner previousOwner
            && context.Owner is LocalSessionOwner nextOwner
            && PathsEqual(_selectedProjectPath, actualPath)
            && object.Equals(previousOwner.LocalSession, nextOwner.LocalSession)
            && object.Equals(previousOwner.Project, nextOwner.Project))
            return;

        var handleChanged = !object.Equals(EngineeringRoot, context.EngineeringRoot);
        SetActiveContext(context);

        if (!PathsEqual(_selectedProjectPath, actualPath))
        {
            _selectedProjectPath = actualPath;
            if (!handleChanged)
            {
                IncrementGeneration();
            }
        }
    }

    private static string? TryReadProjectPathForSelection(ProjectBase project)
    {
        try
        {
            return project.Path?.FullName;
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Could not read a candidate project path: {ex.Message}");
            return null;
        }
    }

    private void SetActiveContext(ActiveProjectContext? context)
    {
        var rootChanged = !object.Equals(EngineeringRoot, context?.EngineeringRoot)
            || _activeContext?.Owner is LocalSessionOwner left && context?.Owner is LocalSessionOwner right
               && !object.Equals(left.LocalSession, right.LocalSession);
        _activeContext = context;
        if (rootChanged)
        {
            IncrementGeneration();
        }
    }

    private void SetPortalHandle(TiaPortal? portal, int? processId)
    {
        if (ReferenceEquals(_tiaPortal, portal) && _attachedProcessId == processId)
        {
            return;
        }

        _tiaPortal = portal;
        _attachedProcessId = processId;
        _portalAttachmentRevision++;
        IncrementGeneration();
    }

    private static bool PathsEqual(string? left, string? right)
    {
        var canonicalLeft = ProjectPathNormalization.Canonicalize(left);
        var canonicalRight = ProjectPathNormalization.Canonicalize(right);
        return canonicalLeft is null && canonicalRight is null
            || canonicalLeft is not null
            && canonicalRight is not null
            && string.Equals(canonicalLeft, canonicalRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatProcessId(int? processId)
        => processId?.ToString() ?? "(none)";

    private void IncrementGeneration()
        => Interlocked.Increment(ref _sessionGeneration);

    private static void OnNotification(object? sender, NotificationEventArgs e)
    {
        Console.Error.WriteLine($"TIA Notification: {e.Text}");
    }

    private void OnConfirmation(object? sender, ConfirmationEventArgs e)
    {
        e.Result = _allowTiaConfirmations
            ? ConfirmationResult.Yes
            : ConfirmationResult.No;
    }

    private void OnDisposed(object? sender, EventArgs e)
    {
        if (_tiaPortal is not null && sender is not null && !ReferenceEquals(sender, _tiaPortal))
        {
            return;
        }

        Console.Error.WriteLine("Attached TIA Portal instance was disposed.");
        SetActiveContext(null);
        _selectedProjectPath = null;
        SetPortalHandle(null, null);
    }

    public void Disconnect()
    {
        if (_tiaPortal is not null)
        {
            var refusal = EvaluatePortalDetach(_tiaPortal);
            if (refusal is not null)
                throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked, refusal);
        }

        ReleaseTemporaryTargetIfSafe();

        if (_tiaPortal != null)
        {
            _tiaPortal.Notification -= OnNotification;
            _tiaPortal.Confirmation -= OnConfirmation;
            _tiaPortal.Disposed -= OnDisposed;
        }

        SetActiveContext(null);
        _selectedProjectPath = null;
        var portal = _tiaPortal;
        SetPortalHandle(null, null);
        portal?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Disconnect();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TiaPortalSession));
        }
    }
}
