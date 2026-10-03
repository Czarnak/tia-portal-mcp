// Locked PR1 compile-only Multiuser foundation; no later operation surface.
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

    public abstract class LocalSessionComposition
    {
        public LocalSession Open(global::System.IO.FileInfo path) => throw new global::System.NotSupportedException();
        public LocalSession OpenServerProject(global::System.IO.FileInfo path) => throw new global::System.NotSupportedException();
    }

    public abstract class LocalSessionInfo
    {
        public global::System.IO.FileInfo ProjectFileInfo => throw new global::System.NotSupportedException();
        public int SessionId => throw new global::System.NotSupportedException();
    }

    public abstract class MultiuserProject : ProjectBase { }
    public abstract class MarkingService { }
    public abstract class ProjectServer { }
    public abstract class ProjectServerComposition { }
}
