namespace TiaMcpServer.OpennessWorker;

/// <summary>
/// Combines response warnings that were set directly during a worker request (e.g. by a thrown
/// <see cref="WorkerOperationException"/>) with warnings captured from stderr during that same
/// request. Kept dependency-free (no Siemens Openness types) so it can be unit tested from
/// <c>TiaMcpServer.Tests</c> without pulling in the rest of <see cref="Program"/>.
/// </summary>
internal static class WorkerWarningMerger
{
    /// <summary>
    /// Returns <paramref name="existing"/> unchanged when nothing was captured. Otherwise
    /// returns <paramref name="existing"/>'s warnings followed by <paramref name="captured"/>'s,
    /// so a deliberate, hand-crafted warning set before the failure (e.g. a
    /// <c>WorkerOperationException</c>'s guidance) is never silently discarded by incidental
    /// stderr output that happened during the same request.
    /// </summary>
    public static List<string>? Merge(List<string>? existing, IReadOnlyCollection<string> captured)
    {
        if (captured.Count == 0)
        {
            return existing;
        }

        return existing is { Count: > 0 }
            ? existing.Concat(captured).ToList()
            : captured.ToList();
    }
}
