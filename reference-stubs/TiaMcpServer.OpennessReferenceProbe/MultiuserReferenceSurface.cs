using System;
using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.Multiuser;

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
            ProjectServerComposition projectServers = portal.ProjectServers;
            Func<FileInfo, LocalSession> open = localSessions.Open;
            Func<FileInfo, LocalSession> openServerProject = localSessions.OpenServerProject;
            MultiuserProject sessionProject = localSession.Project;
            var markingService = localSession.MarkingService;
            Action save = localSession.Save;
            Action close = localSession.Close;
            Func<string, int> closeAndCommit = localSession.CloseAndCommit;
            Func<bool> isUptoDate = localSession.IsUptoDate;
            FileInfo projectFileInfo = sessionInfo.ProjectFileInfo;
            int sessionId = sessionInfo.SessionId;
        }
    }
}
