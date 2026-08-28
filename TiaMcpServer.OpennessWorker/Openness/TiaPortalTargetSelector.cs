using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Siemens-free description of one attachable TIA Portal process. Keeping the selection input
/// free of Openness types lets the net8 test project exercise every ambiguity branch without a
/// live TIA Portal installation.
/// </summary>
internal sealed class TiaPortalProcessCandidate
{
    public TiaPortalProcessCandidate(int id, string? projectPath)
    {
        Id = id;
        ProjectPath = ProjectPathNormalization.Canonicalize(projectPath);
    }

    public int Id { get; }

    public string? ProjectPath { get; }
}

/// <summary>
/// Deterministic, fail-closed selection policy for Portal processes and projects. A caller may
/// select a sole candidate or an exact path match, but never an arbitrary first item.
/// </summary>
internal static class TiaPortalTargetSelector
{
    public static int SelectProcessId(
        IReadOnlyList<TiaPortalProcessCandidate> candidates,
        string? requestedProjectPath)
    {
        if (candidates.Count == 0)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                "No running TIA Portal V21 instance found. Please start TIA Portal before using the MCP server.");
        }

        var requested = ProjectPathNormalization.Canonicalize(requestedProjectPath);
        if (requested is not null)
        {
            var exactMatches = candidates
                .Where(candidate => PathsEqual(candidate.ProjectPath, requested))
                .OrderBy(candidate => candidate.Id)
                .ToList();

            if (exactMatches.Count == 1)
            {
                return exactMatches[0].Id;
            }

            if (exactMatches.Count > 1)
            {
                throw AmbiguousProcessSelection(
                    $"Multiple TIA Portal processes expose the requested project '{requested}'.",
                    candidates);
            }
        }

        if (candidates.Count == 1)
        {
            return candidates[0].Id;
        }

        var reason = requested is null
            ? "Multiple TIA Portal processes are running and no project path uniquely identifies one."
            : $"No running TIA Portal process uniquely exposes requested project '{requested}'.";
        throw AmbiguousProcessSelection(reason, candidates);
    }

    /// <summary>
    /// Returns the unique matching project index, or null when the expected project is not open.
    /// A missing exact match deliberately does not fall back to another open project: the caller
    /// may then apply the existing read-only/open-project policy without ever operating on the
    /// wrong project.
    /// </summary>
    public static int? SelectProjectIndex(
        IReadOnlyList<string?> openProjectPaths,
        string? expectedProjectPath)
    {
        var expected = ProjectPathNormalization.Canonicalize(expectedProjectPath);
        if (expected is not null)
        {
            var matchingIndexes = new List<int>();
            for (var index = 0; index < openProjectPaths.Count; index++)
            {
                if (PathsEqual(openProjectPaths[index], expected))
                {
                    matchingIndexes.Add(index);
                }
            }

            if (matchingIndexes.Count == 1)
            {
                return matchingIndexes[0];
            }

            if (matchingIndexes.Count > 1)
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.TargetAmbiguous,
                    $"Multiple projects in the attached TIA Portal resolve to '{expected}'. No project was selected.");
            }

            return null;
        }

        if (openProjectPaths.Count == 0)
        {
            return null;
        }

        if (openProjectPaths.Count == 1)
        {
            return 0;
        }

        throw new WorkerOperationException(
            WorkerFailureCategories.TargetAmbiguous,
            "Multiple projects are open in the attached TIA Portal and no project path uniquely identifies one. "
            + $"No project was selected. Candidates: {FormatProjectCandidates(openProjectPaths)}.");
    }

    private static WorkerOperationException AmbiguousProcessSelection(
        string reason,
        IReadOnlyList<TiaPortalProcessCandidate> candidates)
        => new(
            WorkerFailureCategories.TargetAmbiguous,
            reason + " No process was attached. Candidates: " + FormatProcessCandidates(candidates)
            + ". Specify an exact project path or close the unrelated TIA Portal instance.");

    private static string FormatProcessCandidates(IReadOnlyList<TiaPortalProcessCandidate> candidates)
        => string.Join(
            "; ",
            candidates
                .OrderBy(candidate => candidate.Id)
                .ThenBy(candidate => candidate.ProjectPath, StringComparer.OrdinalIgnoreCase)
                .Select(candidate => $"PID {candidate.Id} project='{candidate.ProjectPath ?? "(none)"}'"));

    private static string FormatProjectCandidates(IReadOnlyList<string?> paths)
        => string.Join(
            "; ",
            paths.Select((path, index) => $"index {index} path='{path ?? "(unknown)"}'"));

    private static bool PathsEqual(string? left, string? right)
    {
        var canonicalLeft = ProjectPathNormalization.Canonicalize(left);
        var canonicalRight = ProjectPathNormalization.Canonicalize(right);
        return canonicalLeft is not null
            && canonicalRight is not null
            && string.Equals(canonicalLeft, canonicalRight, StringComparison.OrdinalIgnoreCase);
    }
}
