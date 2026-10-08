// Compile-only Multiuser foundation and verified PR3 inventory surface.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering
{
    public abstract partial class TiaPortal
    {
        public global::Siemens.Engineering.Multiuser.LocalSessionComposition LocalSessions => throw new global::System.NotSupportedException();
        public global::Siemens.Engineering.Multiuser.ProjectServerComposition ProjectServers => throw new global::System.NotSupportedException();
    }
}

namespace Siemens.Engineering.Multiuser
{
    public abstract class LocalSession
    {
        public MultiuserProject Project => throw new global::System.NotSupportedException();
        public MarkingService MarkingService => throw new global::System.NotSupportedException();
        public void Save() => throw new global::System.NotSupportedException();
        public void Close() => throw new global::System.NotSupportedException();
        public int CloseAndCommit(string commitComment) => throw new global::System.NotSupportedException();
        public bool IsUptoDate() => throw new global::System.NotSupportedException();
    }

    public abstract class LocalSessionComposition : global::System.Collections.Generic.IEnumerable<LocalSession>
    {
        public LocalSession Open(global::System.IO.FileInfo path) => throw new global::System.NotSupportedException();
        public LocalSession OpenServerProject(global::System.IO.FileInfo path) => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<LocalSession> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public abstract class LocalSessionInfo
    {
        public global::System.IO.FileInfo ProjectFileInfo => throw new global::System.NotSupportedException();
        public int SessionId => throw new global::System.NotSupportedException();
    }

    public abstract class MultiuserProject : ProjectBase { }
    public abstract class MarkingService { }
    public abstract class ProjectServer
    {
        public string ServerName => throw new global::System.NotSupportedException();
        public string Host => throw new global::System.NotSupportedException();
        public int Port => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IList<ProjectServerGroup> GetProjectServerGroups() => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IList<ServerProjectInfo> GetServerProjects() => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IList<LocalSessionInfo> GetLocalSessions(ServerProjectInfo project) => throw new global::System.NotSupportedException();
        public LockStateProvider GetLockStateProvider(ServerProjectInfo project) => throw new global::System.NotSupportedException();
    }

    public abstract class ProjectServerComposition : global::System.Collections.Generic.IEnumerable<ProjectServer>
    {
        public global::System.Collections.Generic.IEnumerator<ProjectServer> GetEnumerator() => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public abstract class ProjectServerGroup
    {
        public string Name => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IList<ServerProjectInfo> GetServerProjects() => throw new global::System.NotSupportedException();
    }

    public abstract class ServerProjectInfo
    {
        public string ProjectName => throw new global::System.NotSupportedException();
        public string ServerAlias => throw new global::System.NotSupportedException();
    }

    public abstract class LockStateProvider
    {
        public bool IsProjectLocked() => throw new global::System.NotSupportedException();
        public string GetLockOwner() => throw new global::System.NotSupportedException();
    }
}
