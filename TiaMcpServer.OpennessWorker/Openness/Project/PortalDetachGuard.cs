namespace TiaMcpServer.OpennessWorker.Openness;

internal static class PortalDetachGuard
{
    // Null denotes an unreadable project collection; an empty collection proves no project is at risk.
    public static string? EvaluateProjects(bool hasUserInterface, int otherClientCount,
        IReadOnlyList<bool?>? projectModifiedStates, bool hasLocalSessions = false)
    {
        if (!hasUserInterface && otherClientCount == 0 && hasLocalSessions)
            return "Cannot detach the last client from a headless TIA Portal with an open local session whose preservation is unproved.";
        var hasProject = projectModifiedStates is null || projectModifiedStates.Count > 0;
        var allUnmodified = projectModifiedStates is not null && projectModifiedStates.All(state => state == false);
        return Evaluate(hasUserInterface, otherClientCount, hasProject, allUnmodified ? false : (bool?)null);
    }

    public static string? Evaluate(bool hasUserInterface, int otherClientCount, bool hasProject, bool? projectIsModified)
        => !hasUserInterface && otherClientCount == 0 && hasProject && projectIsModified != false
            ? "Cannot detach the last client from a headless TIA Portal with an unsaved or unknown project state. Save the project or keep another client attached before switching."
            : null;
}
