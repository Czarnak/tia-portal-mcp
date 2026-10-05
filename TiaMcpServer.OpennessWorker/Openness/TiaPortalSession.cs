using Siemens.Engineering;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public class TiaPortalSession : IDisposable
{
    private readonly bool _allowTiaConfirmations;
    private readonly string _workerSessionId = Guid.NewGuid().ToString("N");
    private TiaPortal? _tiaPortal;
    private ActiveProjectContext? _activeContext;
    private bool _disposed;
    private int? _attachedProcessId;
    private string? _selectedProjectPath;
    private long _sessionGeneration;

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
            ProjectPath = liveProjectPath
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

    public void Connect(string? requestedProjectPath)
    {
        ThrowIfDisposed();

        if (IsConnected)
        {
            return;
        }

        var inventory = TiaPortalProcessInventory.Read();
        var candidates = inventory.Select(entry => entry.Candidate).ToList();
        var selectedProcessId = TiaPortalTargetSelector.SelectProcessId(candidates, requestedProjectPath);
        var entry = inventory.FirstOrDefault(item => item.Candidate.Id == selectedProcessId);
        var selectedProcess = entry?.Process;
        var advertisedProjectPath = entry?.Candidate.ProjectPath;

        if (selectedProcess is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.PostconditionFailed,
                $"TIA Portal selector chose PID {selectedProcessId}, but that process was no longer available for Attach().");
        }

        var attachedPortal = selectedProcess.Attach();
        var attachedProcessId = attachedPortal.GetCurrentProcess().Id;
        if (attachedProcessId != selectedProcessId)
        {
            attachedPortal.Dispose();
            throw new WorkerOperationException(
                WorkerFailureCategories.BindingConflict,
                $"TIA Portal Attach() targeted PID {selectedProcessId} but returned PID {attachedProcessId}. No operation was performed.");
        }

        SetPortalHandle(attachedPortal, attachedProcessId);
        attachedPortal.Notification += OnNotification;
        attachedPortal.Confirmation += OnConfirmation;
        attachedPortal.Disposed += OnDisposed;
        // Projects present when we attach belong to the TIA Portal UI, never this worker.
        SelectOpenProject(
            ProjectPathNormalization.Canonicalize(requestedProjectPath) ?? advertisedProjectPath);

        Console.Error.WriteLine(
            $"Connected to TIA Portal PID {_attachedProcessId}"
            + $" with project '{CurrentProjectPath ?? "(none)"}'.");
    }

    private WorkerSessionIdentity GetCachedSessionIdentity()
        => new()
        {
            WorkerSessionId = _workerSessionId,
            SessionGeneration = Interlocked.Read(ref _sessionGeneration),
            PortalProcessId = _attachedProcessId,
            ProjectPath = _selectedProjectPath
        };

    public PortalProjectSelectionInfo SelectPortalProject(string projectPath)
    {
        ThrowIfDisposed();

        // Lookup precedes every session transition, including the first attach and same-PID selection.
        var inventory = TiaPortalProcessInventory.Read();
        var selectedProcessId = TiaPortalTargetSelector.SelectExactProcessId(
            inventory.Select(entry => entry.Candidate).ToList(), projectPath);
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
            var projects = _tiaPortal!.Projects.ToList();
            var selectedIndex = TiaPortalTargetSelector.SelectProjectIndex(
                projects.Select(TryReadProjectPathForSelection).ToList(), projectPath);
            if (selectedIndex is null)
            {
                throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                    $"Requested project '{projectPath}' is no longer open in TIA Portal PID {selectedProcessId}. No project was selected.");
            }

            AdoptProject(projects[selectedIndex.Value], openedByWorker: false, projectPath);
            return previous;
        }

        if (IsConnected)
        {
            var hasUserInterface = false;
            var otherClientCount = 0;
            try
            {
                var attachedProcess = _tiaPortal!.GetCurrentProcess();
                hasUserInterface = TiaPortalProcessInventory.TryReadHasUserInterface(attachedProcess);
                var workerProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
                otherClientCount = attachedProcess.AttachedSessions
                    .Count((Siemens.Engineering.TiaPortalSession session) => session.ProcessId != workerProcessId);
            }
            catch (Exception ex)
            {
                // An unreadable process/client state must not justify detaching the last headless client.
                hasUserInterface = false;
                otherClientCount = 0;
                Console.Error.WriteLine($"Could not read attached Portal clients: {ex.Message}");
            }

            var refusal = PortalDetachGuard.EvaluateProjects(hasUserInterface, otherClientCount,
                ReadAttachedProjectModifiedStates());
            if (refusal is not null)
            {
                throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked, refusal);
            }
        }

        // From this point a former binding cannot be retained on any failure, including Dispose/Attach.
        try
        {
            if (IsConnected)
            {
                Disconnect();
            }

            var portal = selectedProcess.Attach();
            try
            {
                var actualProcessId = portal.GetCurrentProcess().Id;
                if (actualProcessId != selectedProcessId)
                {
                    throw new InvalidOperationException($"TIA Portal Attach() targeted PID {selectedProcessId} but returned PID {actualProcessId}.");
                }

                SetPortalHandle(portal, actualProcessId);
                portal.Notification += OnNotification;
                portal.Confirmation += OnConfirmation;
                portal.Disposed += OnDisposed;
            }
            catch
            {
                portal.Dispose();
                throw;
            }

            var projects = portal.Projects.ToList();
            var selectedIndex = TiaPortalTargetSelector.SelectProjectIndex(
                projects.Select(TryReadProjectPathForSelection).ToList(), projectPath);
            if (selectedIndex is null)
            {
                throw new InvalidOperationException($"Requested project '{projectPath}' is no longer open in TIA Portal PID {selectedProcessId}.");
            }

            AdoptProject(projects[selectedIndex.Value], openedByWorker: false, projectPath);
            previous.Reattached = true;
            return previous;
        }
        catch (Exception ex)
        {
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

    private IReadOnlyList<bool?>? ReadAttachedProjectModifiedStates()
    {
        try { return _tiaPortal!.Projects.Select(TryReadProjectIsModified).ToList(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read attached Portal projects: {ex.Message}");
            return null;
        }
    }

    public void OpenProject(string projectPath)
    {
        ThrowIfDisposed();
        if (_activeContext is not null)
            RequireStandaloneOwner();

        if (!IsConnected)
        {
            Connect(projectPath);
        }

        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException("TIA Portal project file was not found.", projectPath);
        }

        var requestedPath = Path.GetFullPath(projectPath);
        var currentPath = TryReadCurrentProjectPath();
        if (currentPath is not null &&
            string.Equals(currentPath, requestedPath, StringComparison.OrdinalIgnoreCase))
        {
            // Persistent session: the requested project is already open — reuse it.
            return;
        }

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

            SetActiveContext(null);
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
        if (_activeContext is not null)
            RequireStandaloneOwner();

        var destination = ProjectPathNormalization.Canonicalize(destinationProjectPath)
            ?? throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "A destination project path is required for the rebind-state probe.");
        var source = ProjectPathNormalization.Canonicalize(TryReadCurrentProjectPath());
        if (source is null)
        {
            return ProjectRebindStateInfo.Create(null, destination, null, sourceOpenedByWorker: false);
        }

        var currentProject = RequireStandaloneOwner().Project;
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

        return ProjectRebindStateInfo.Create(source, destination, isModified, _activeContext?.Owner.OpenedByWorker == true);
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

    public void EnsureConnected(string? requestedProjectPath)
    {
        ThrowIfDisposed();

        if (!IsConnected)
        {
            Connect(requestedProjectPath);
            return;
        }

        var actualProcessId = _tiaPortal!.GetCurrentProcess().Id;
        if (_attachedProcessId != actualProcessId)
        {
            var expectedProcessId = _attachedProcessId;
            _attachedProcessId = actualProcessId;
            IncrementGeneration();
            throw new WorkerOperationException(
                WorkerFailureCategories.BindingConflict,
                $"The attached TIA Portal PID changed from {FormatProcessId(expectedProcessId)} "
                + $"to {actualProcessId}. No operation was performed.");
        }

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

        var projects = _tiaPortal.Projects.ToList();
        var paths = new List<string?>(projects.Count);
        foreach (var project in projects)
        {
            paths.Add(TryReadProjectPathForSelection(project));
        }

        var selectedIndex = TiaPortalTargetSelector.SelectProjectIndex(paths, expectedProjectPath);
        if (selectedIndex is null)
        {
            SetActiveContext(null);
            return;
        }

        AdoptProject(
            projects[selectedIndex.Value],
            openedByWorker: false,
            expectedProjectPath);
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

        var handleChanged = !ReferenceEquals(EngineeringRoot, context.EngineeringRoot);
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
        var rootChanged = !ReferenceEquals(EngineeringRoot, context?.EngineeringRoot);
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
        _disposed = true;
        Disconnect();
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
