// Offline boundary objects model the verified V21 signatures, never a live server.
using Siemens.Engineering.Multiuser;

namespace Siemens.Engineering
{
    public sealed partial class TiaPortal
    {
        public ProjectServerComposition ProjectServers { get; } = new();
    }
}

namespace Siemens.Engineering.Multiuser
{
    public sealed class ProjectServerComposition : IEnumerable<ProjectServer>
    {
        public List<ProjectServer> Items { get; } = new();
        public Exception? EnumerationFailure { get; set; }
        public IEnumerator<ProjectServer> GetEnumerator()
        {
            foreach (var item in Items) yield return item;
            if (EnumerationFailure is not null) throw EnumerationFailure;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public sealed class ProjectServer
    {
        public string ServerName { get; set; } = "Fixture";
        public string Host { get; set; } = "server.example";
        public int Port { get; set; } = 8735;
        public IList<ProjectServerGroup> Groups { get; set; } = new List<ProjectServerGroup>();
        public IList<ServerProjectInfo> Projects { get; set; } = new List<ServerProjectInfo>();
        public IList<LocalSessionInfo> Sessions { get; set; } = new List<LocalSessionInfo>();
        public LockStateProvider Lock { get; set; } = new();
        public Exception? RemoteFailure { get; set; }
        public Action? OnRead { get; set; }
        public ServerProjectInfo? LastSessionProject { get; private set; }
        public ServerProjectInfo? LastLockProject { get; private set; }
        public int RemoteCalls { get; private set; }
        private void Read() { RemoteCalls++; OnRead?.Invoke(); if (RemoteFailure is not null) throw RemoteFailure; }
        public IList<ProjectServerGroup> GetProjectServerGroups() { Read(); return Groups; }
        public IList<ServerProjectInfo> GetServerProjects() { Read(); return Projects; }
        public IList<LocalSessionInfo> GetLocalSessions(ServerProjectInfo project) { Read(); LastSessionProject = project; return Sessions; }
        public LockStateProvider GetLockStateProvider(ServerProjectInfo project) { Read(); LastLockProject = project; return Lock; }
    }
    public sealed class ProjectServerGroup
    {
        public string Name { get; set; } = "Group";
        public IList<ServerProjectInfo> Projects { get; set; } = new List<ServerProjectInfo>();
        public Exception? Failure { get; set; }
        public IList<ServerProjectInfo> GetServerProjects() => Failure is null ? Projects : throw Failure;
    }
    public sealed class ServerProjectInfo
    {
        public string ProjectName { get; set; } = "Demo";
        public string ServerAlias { get; set; } = "Fixture";
    }
    public sealed class LocalSessionInfo
    {
        public FileInfo ProjectFileInfo { get; set; } = null!;
        public int SessionId { get; set; }
    }
    public sealed class LockStateProvider
    {
        public Queue<bool> States { get; } = new();
        public bool Locked { get; set; }
        public string Owner { get; set; } = "owner";
        public Exception? OwnerFailure { get; set; }
        public int OwnerReads { get; private set; }
        public bool IsProjectLocked() => States.Count > 0 ? States.Dequeue() : Locked;
        public string GetLockOwner() { OwnerReads++; return OwnerFailure is null ? Owner : throw OwnerFailure; }
    }
}
