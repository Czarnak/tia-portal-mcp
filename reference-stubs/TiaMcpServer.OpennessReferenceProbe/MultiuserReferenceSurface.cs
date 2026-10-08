using System;
using System.IO;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.Multiuser;
using Siemens.Engineering.HW;

namespace TiaMcpServer.OpennessReferenceProbe
{
    // Compile-only signature checks. This method is never called and the probe has no entry point.
    internal static class MultiuserReferenceSurface
    {
        private static void Foundation(
            Project standaloneProject,
            MultiuserProject multiuserProject,
            TiaPortal portal,
            LocalSession localSession,
            LocalSessionInfo sessionInfo,
            ProjectServer projectServer)
        {
            ProjectBase standaloneBase = standaloneProject;
            ProjectBase multiuserBase = multiuserProject;
            LocalSessionComposition localSessions = portal.LocalSessions;
            IEnumerable<LocalSession> openOwners = localSessions;
            Func<IEnumerator<LocalSession>> enumerateOwners = localSessions.GetEnumerator;
            ProjectServerComposition projectServers = portal.ProjectServers;
            Func<FileInfo, LocalSession> open = localSessions.Open;
            Func<FileInfo, LocalSession> openServerProject = localSessions.OpenServerProject;
            MultiuserProject sessionProject = localSession.Project;
            FileInfo engineeringProjectPath = sessionProject.Path;
            var markingService = localSession.MarkingService;
            Action save = localSession.Save;
            Action close = localSession.Close;
            Func<string, int> closeAndCommit = localSession.CloseAndCommit;
            Func<bool> isUptoDate = localSession.IsUptoDate;
            FileInfo projectFileInfo = sessionInfo.ProjectFileInfo;
            int sessionId = sessionInfo.SessionId;
        }

        // Compile-only inventory checks; no API invocation or object construction.
        private static void Inventory(TiaPortal portal, ProjectServer server,
            ProjectServerGroup group, ServerProjectInfo project, LockStateProvider locks)
        {
            IEnumerable<ProjectServer> servers = portal.ProjectServers;
            Func<IEnumerator<ProjectServer>> enumerate = portal.ProjectServers.GetEnumerator;
            string alias = server.ServerName;
            string host = server.Host;
            int port = server.Port;
            Func<IList<ProjectServerGroup>> groups = server.GetProjectServerGroups;
            string groupName = group.Name;
            Func<IList<ServerProjectInfo>> rootProjects = server.GetServerProjects;
            Func<IList<ServerProjectInfo>> groupProjects = group.GetServerProjects;
            string projectName = project.ProjectName;
            string projectAlias = project.ServerAlias;
            Func<ServerProjectInfo, IList<LocalSessionInfo>> sessions = server.GetLocalSessions;
            Func<ServerProjectInfo, LockStateProvider> lockProvider = server.GetLockStateProvider;
            Func<bool> isLocked = locks.IsProjectLocked;
            Func<string> owner = locks.GetLockOwner;
        }

        // Compile-only compatibility check; never called.
        private static void IoSystemSubnet(IoSystem ioSystem)
        {
            Subnet subnet = ioSystem.Subnet;
        }
    }
}
