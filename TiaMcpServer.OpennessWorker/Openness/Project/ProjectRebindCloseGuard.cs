using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Keeps a session's current project ownership intact unless close succeeds.</summary>
internal static class ProjectRebindCloseGuard
{
    internal static void CloseBeforeRebind(
        Func<bool> isCurrentModified,
        Action closeCurrent,
        Action openReplacement)
    {
        bool isModified;
        try
        {
            isModified = isCurrentModified();
        }
        catch (Exception)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                "Could not verify whether the current worker-owned project has unsaved changes. "
                + "No project was closed; inspect its state before requesting a fresh preview.");
        }

        if (isModified)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.StateChanged,
                "The current worker-owned project has unsaved changes. "
                + "Save it explicitly and request a fresh preview before rebinding.");
        }

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
