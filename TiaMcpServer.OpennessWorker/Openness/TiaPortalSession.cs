using System;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using TiaMcpServer.OpennessWorker;

namespace TiaMcpServer.OpennessWorker.Openness;

public class TiaPortalSession : IDisposable
{
    private readonly bool _allowTiaConfirmations;
    private TiaPortal? _tiaPortal;
    private bool _disposed;
    private bool _projectOpenedByWorker;

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

    public Project? Project { get; internal set; }

    public TiaPortal? TiaPortal => _tiaPortal;

    public bool IsConnected => _tiaPortal != null;

    public void Connect()
    {
        ThrowIfDisposed();

        if (IsConnected)
        {
            return;
        }

        var processes = TiaPortal.GetProcesses();
        if (!processes.Any())
        {
            throw new InvalidOperationException("No running TIA Portal V21 instance found. Please start TIA Portal before using the MCP server.");
        }

        _tiaPortal = processes.First().Attach();
        _tiaPortal.Notification += OnNotification;
        _tiaPortal.Confirmation += OnConfirmation;
        _tiaPortal.Disposed += OnDisposed;
        // Projects present when we attach belong to the TIA Portal UI, never this worker.
        _projectOpenedByWorker = false;
        Project = _tiaPortal.Projects.FirstOrDefault();

        Console.Error.WriteLine("Connected to running TIA Portal instance.");
    }

    public void OpenProject(string projectPath)
    {
        ThrowIfDisposed();

        if (!IsConnected)
        {
            Connect();
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
                            Project = _tiaPortal!.Projects.Open(new FileInfo(requestedPath));
                            _projectOpenedByWorker = true;
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

        Project = _tiaPortal!.Projects.Open(new FileInfo(requestedPath));
        _projectOpenedByWorker = true;
    }

    /// <summary>Absolute path of the attached project, or null when nothing is attached.</summary>
    public string? CurrentProjectPath => TryReadCurrentProjectPath();

    private string? TryReadCurrentProjectPath()
    {
        if (Project is null)
        {
            return null;
        }

        try
        {
            return Project.Path?.FullName;
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
        Project = null;
        _projectOpenedByWorker = false;
    }

    public void EnsureConnected()
    {
        ThrowIfDisposed();

        if (!IsConnected)
        {
            Connect();
        }
    }

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
        Console.Error.WriteLine("Attached TIA Portal instance was disposed.");
        _tiaPortal = null;
        Project = null;
        _projectOpenedByWorker = false;
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
        _tiaPortal?.Dispose();
        _tiaPortal = null;
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
