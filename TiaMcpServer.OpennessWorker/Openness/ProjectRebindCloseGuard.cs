using System;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Keeps a session's current project ownership intact unless close succeeds.</summary>
internal static class ProjectRebindCloseGuard
{
    internal static void CloseBeforeRebind(Action closeCurrent, Action openReplacement)
    {
        try
        {
            closeCurrent();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not close the previous worker-owned project: {ex.Message}", ex);
        }

        openReplacement();
    }
}
