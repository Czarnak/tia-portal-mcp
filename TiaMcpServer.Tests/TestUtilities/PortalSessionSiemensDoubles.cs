// Executable boundary doubles only. Tests never load the compile-only Siemens references.
namespace Siemens.Engineering
{
    public abstract class ProjectBase
    {
        private FileInfo? path;
        private bool isModified;
        public Exception? PathFailure { get; set; }
        public Exception? ModifiedFailure { get; set; }
        public List<string> Calls { get; set; } = new();
        public FileInfo? Path
        {
            get => PathFailure is null ? path : throw PathFailure;
            set => path = value;
        }
        public bool IsModified
        {
            get { Calls.Add("IsModified"); return ModifiedFailure is null ? isModified : throw ModifiedFailure; }
            set => isModified = value;
        }
    }
    public sealed partial class Project
    {
        public int SaveCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public Exception? CloseFailure { get; set; }
        public Action? OnClose { get; set; }
        public void Save() { SaveCalls++; Calls.Add("Save"); }
        public void Close()
        {
            CloseCalls++; Calls.Add("Close");
            if (CloseFailure is not null) throw CloseFailure;
            OnClose?.Invoke();
        }
    }

    public enum TiaPortalMode { WithUserInterface, WithoutUserInterface }
    public enum ConfirmationResult { Yes, No }
    public sealed class NotificationEventArgs : EventArgs { public string Text { get; set; } = ""; }
    public sealed class ConfirmationEventArgs : EventArgs { public ConfirmationResult Result { get; set; } }
    public sealed class TiaPortalSession { public int ProcessId { get; set; } }

    public sealed class ProjectComposition : IEnumerable<Project>
    {
        public List<Project> Items { get; } = new();
        public List<string> Calls { get; } = new();
        public int OpenCalls { get; private set; }
        public Exception? OpenFailure { get; set; }
        public Project Open(FileInfo file)
        {
            OpenCalls++; Calls.Add("Open");
            if (OpenFailure is not null) throw OpenFailure;
            var project = new Project { Path = file, Calls = Calls };
            project.OnClose = () => Items.Remove(project);
            Items.Add(project);
            return project;
        }
        public IEnumerator<Project> GetEnumerator() => Items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class TiaPortalProcess
    {
        public int Id { get; set; }
        public TiaPortalMode Mode { get; set; } = TiaPortalMode.WithUserInterface;
        public FileInfo? ProjectPath { get; set; }
        public List<TiaPortalSession> AttachedSessions { get; } = new();
        public TiaPortal Portal { get; set; } = null!;
        public Exception? AttachFailure { get; set; }
        public int AttachCalls { get; private set; }
        public TiaPortal Attach()
        {
            AttachCalls++;
            if (AttachFailure is not null) throw AttachFailure;
            return Portal;
        }
    }

    public sealed partial class TiaPortal : IDisposable
    {
        public static List<TiaPortalProcess> Processes { get; } = new();
        public static IEnumerable<TiaPortalProcess> GetProcesses() => Processes;
        public TiaPortalProcess Process { get; set; } = null!;
        public Exception? CurrentProcessFailure { get; set; }
        public TiaPortalProcess GetCurrentProcess() => CurrentProcessFailure is null ? Process : throw CurrentProcessFailure;
        public ProjectComposition Projects { get; } = new();
        public Siemens.Engineering.Multiuser.LocalSessionComposition LocalSessions { get; } = new();
        public event EventHandler<NotificationEventArgs>? Notification;
        public event EventHandler<ConfirmationEventArgs>? Confirmation;
        public event EventHandler? Disposed;
        public int DisposeCalls { get; private set; }
        public int? SubscribersAtDispose { get; private set; }
        public Exception? DisposeFailure { get; set; }
        public void Dispose()
        {
            DisposeCalls++;
            SubscribersAtDispose = (Notification?.GetInvocationList().Length ?? 0)
                + (Confirmation?.GetInvocationList().Length ?? 0) + (Disposed?.GetInvocationList().Length ?? 0);
            if (DisposeFailure is not null) throw DisposeFailure;
            Disposed?.Invoke(this, EventArgs.Empty);
        }
        public void RaiseDisposed() => Disposed?.Invoke(this, EventArgs.Empty);
        public ConfirmationResult RaiseConfirmation()
        {
            var args = new ConfirmationEventArgs(); Confirmation?.Invoke(this, args); return args.Result;
        }
        public void RaiseNotification() => Notification?.Invoke(this, new NotificationEventArgs());
    }

}

namespace Siemens.Engineering.Multiuser
{
    public sealed class MultiuserProject : ProjectBase
    {
        public string? EqualityToken { get; set; }
        public override bool Equals(object? other) => ReferenceEquals(this, other)
            || EqualityToken is not null && other is MultiuserProject project && EqualityToken == project.EqualityToken;
        public override int GetHashCode() => EqualityToken?.GetHashCode() ?? base.GetHashCode();
    }
    public sealed class LocalSessionComposition : IEnumerable<LocalSession>
    {
        public List<LocalSession> Items { get; } = new();
        public int OpenCalls { get; private set; }
        public FileInfo? LastOpenedFile { get; private set; }
        public FileInfo? NextProjectPath { get; set; }
        public Exception? OpenFailure { get; set; }
        public Action<int>? OnEnumerate { get; set; }
        public int EnumerationCount { get; private set; }
        public LocalSession Open(FileInfo file)
        {
            OpenCalls++;
            LastOpenedFile = file;
            if (OpenFailure is not null) throw OpenFailure;
            var owner = new LocalSession { Project = new MultiuserProject { Path = NextProjectPath } };
            Items.Add(owner);
            return owner;
        }
        public IEnumerator<LocalSession> GetEnumerator()
        {
            OnEnumerate?.Invoke(++EnumerationCount);
            return Items.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public sealed class LocalSession
    {
        public MultiuserProject Project { get; set; } = new();
        public string? EqualityToken { get; set; }
        public override bool Equals(object? other) => ReferenceEquals(this, other)
            || EqualityToken is not null && other is LocalSession session && EqualityToken == session.EqualityToken;
        public override int GetHashCode() => EqualityToken?.GetHashCode() ?? base.GetHashCode();
        public int SaveCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public int CommitCalls { get; private set; }
        public void Save() => SaveCalls++;
        public void Close() => CloseCalls++;
        public void CloseAndCommit() => CommitCalls++;
    }
}
