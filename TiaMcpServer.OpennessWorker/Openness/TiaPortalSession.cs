using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Siemens.Engineering;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;

namespace TiaMcpServer.OpennessWorker.Openness;

public class TiaPortalSession : IDisposable
{
    private readonly bool _allowTiaConfirmations;
    private readonly string _workerSessionId = Guid.NewGuid().ToString("N");
    private TiaPortal? _tiaPortal;
    private Project? _project;
    private bool _disposed;
    private bool _projectOpenedByWorker;
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

    public Project? Project
    {
        get => _project;
        internal set => SetProjectHandle(value);
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
        bool allowMissingExpectedIdentity)
    {
        if (expected is null)
        {
            if (allowMissingExpectedIdentity)
            {
                return;
            }

            throw new WorkerOperationException(
                WorkerFailureCategories.BindingConflict,
                "This operation requires the exact WorkerSessionIdentity returned by a successful "
                + "get_project_status/open_project/create_project call.");
        }

        var current = GetSessionIdentity();
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

        var processes = TiaPortal.GetProcesses().ToList();
        var candidates = new List<TiaPortalProcessCandidate>(processes.Count);
        foreach (var process in processes)
        {
            candidates.Add(new TiaPortalProcessCandidate(
                process.Id,
                TryReadAdvertisedProjectPath(process)));
        }

        var selectedProcessId = TiaPortalTargetSelector.SelectProcessId(candidates, requestedProjectPath);
        TiaPortalProcess? selectedProcess = null;
        string? advertisedProjectPath = null;
        for (var index = 0; index < processes.Count; index++)
        {
            if (processes[index].Id != selectedProcessId)
            {
                continue;
            }

            selectedProcess = processes[index];
            advertisedProjectPath = candidates[index].ProjectPath;
            break;
        }

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
        _projectOpenedByWorker = false;
        SelectOpenProject(
            ProjectPathNormalization.Canonicalize(requestedProjectPath) ?? advertisedProjectPath);

        Console.Error.WriteLine(
            $"Connected to TIA Portal PID {_attachedProcessId}"
            + $" with project '{CurrentProjectPath ?? "(none)"}'.");
    }

    public void OpenProject(string projectPath)
    {
        ThrowIfDisposed();

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
            if (_projectOpenedByWorker)
            {
                Console.Error.WriteLine($"Closing project '{currentPath ?? "(unknown)"}' before opening '{requestedPath}'.");
                try
                {
                    ProjectRebindCloseGuard.CloseBeforeRebind(
                        Project.Close,
                        () =>
                        {
                            Project = null;
                            _projectOpenedByWorker = false;
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

            Project = null;
            _projectOpenedByWorker = false;
        }

        var project = _tiaPortal!.Projects.Open(new FileInfo(requestedPath));
        AdoptProject(project, openedByWorker: true, requestedPath);
    }

    /// <summary>Absolute path of the attached project, or null when nothing is attached.</summary>
    public string? CurrentProjectPath => TryReadCurrentProjectPath();

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
        if (Project is null)
        {
            return null;
        }

        try
        {
            var currentPath = Project.Path?.FullName;
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                return currentPath;
            }

            // A live project used for a bound engineering operation must always have a readable
            // identity. Treat a null/blank path exactly like a stale handle: clearing the handle
            // advances the generation, so the post-refresh ExpectedSessionIdentity check fails
            // before the operation body can touch an unidentified project.
            Project = null;
            _projectOpenedByWorker = false;
            return null;
        }
        catch (EngineeringException)
        {
            // Stale handle: the project was closed in the TIA Portal UI since we opened it.
            Project = null;
            _projectOpenedByWorker = false;
            return null;
        }
    }

    internal void MarkProjectClosed()
    {
        var hadProjectHandle = Project is not null;
        var hadSelectedProject = _selectedProjectPath is not null;
        Project = null;
        _projectOpenedByWorker = false;
        _selectedProjectPath = null;
        if (hadSelectedProject && !hadProjectHandle)
        {
            // SetProjectHandle already advanced the generation when a live handle was cleared.
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

        if (Project is not null)
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

        if (Project is null)
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
            Project = null;
            _projectOpenedByWorker = false;
            return;
        }

        AdoptProject(
            projects[selectedIndex.Value],
            openedByWorker: false,
            expectedProjectPath);
    }

    private void AdoptProject(Project project, bool openedByWorker, string? expectedProjectPath)
    {
        var actualPath = ProjectPathNormalization.Canonicalize(TryReadProjectPathForSelection(project));
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

        var handleChanged = !ReferenceEquals(Project, project);
        Project = project;
        _projectOpenedByWorker = openedByWorker;

        if (!PathsEqual(_selectedProjectPath, actualPath))
        {
            _selectedProjectPath = actualPath;
            if (!handleChanged)
            {
                IncrementGeneration();
            }
        }
    }

    private static string? TryReadAdvertisedProjectPath(TiaPortalProcess process)
    {
        try
        {
            return process.ProjectPath?.FullName;
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine(
                $"Could not read advertised project path for TIA Portal PID {process.Id}: {ex.Message}");
            return null;
        }
    }

    private static string? TryReadProjectPathForSelection(Project project)
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

    private void SetProjectHandle(Project? project)
    {
        if (ReferenceEquals(_project, project))
        {
            return;
        }

        _project = project;
        IncrementGeneration();
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
        Project = null;
        _projectOpenedByWorker = false;
        _selectedProjectPath = null;
        SetPortalHandle(null, null);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (disposing && _tiaPortal != null)
        {
            _tiaPortal.Notification -= OnNotification;
            _tiaPortal.Confirmation -= OnConfirmation;
            _tiaPortal.Disposed -= OnDisposed;
        }

        Project = null;
        _projectOpenedByWorker = false;
        _selectedProjectPath = null;
        var portal = _tiaPortal;
        SetPortalHandle(null, null);
        portal?.Dispose();
    }

    public void Dispose()
    {
        Dispose(true);
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
