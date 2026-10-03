using TiaMcpServer.Contracts;

namespace TiaMcpServer.Worker;

public static class ProjectBindingTransitions
{
    public const string Bound = "bound";
    public const string Unchanged = "unchanged";
    public const string Switched = "switched";
    public const string None = "none";
}

internal abstract record BindingStep
{
    public sealed record Reverify : BindingStep;
    public sealed record ListThenSelect : BindingStep;
    public sealed record Select(string Path, bool IsSwitch) : BindingStep;
    public sealed record Reject(string Category, string Message) : BindingStep;
}

internal abstract record ListingChoice
{
    public sealed record Select(string Path) : ListingChoice;
    public sealed record NotFound : ListingChoice;
    public sealed record Ambiguous : ListingChoice;
}

internal static class ProjectBindingDecision
{
    internal static IReadOnlySet<string> PreDetachRefusalCategories { get; } =
        System.Collections.Frozen.FrozenSet.ToFrozenSet(new[]
        {
            WorkerFailureCategories.TargetNotFound, WorkerFailureCategories.TargetAmbiguous,
            WorkerFailureCategories.GuardBlocked
        }, StringComparer.Ordinal);

    internal static BindingStep Decide(ProjectBindingSnapshot current, string? requestedPath, bool forceRebind)
    {
        var requested = ProjectPathNormalization.Canonicalize(requestedPath);
        var retained = ProjectPathNormalization.Canonicalize(current.ProjectPath);
        if (current.State == ProjectBindingSnapshot.UnboundState)
            return requested is null ? new BindingStep.ListThenSelect() : new BindingStep.Select(requested, false);
        if (current.State is not (ProjectBindingSnapshot.ConfiguredUnverifiedState
            or ProjectBindingSnapshot.InvalidatedState or ProjectBindingSnapshot.VerifiedState) || retained is null)
            return new BindingStep.Reject(WorkerFailureCategories.BindingConflict, "The session binding state cannot select an open project.");
        var same = requested is null || string.Equals(requested, retained, StringComparison.OrdinalIgnoreCase);
        if (!same && !forceRebind)
            return new BindingStep.Reject(WorkerFailureCategories.BindingConflict, "A different project requires forceRebind=true.");
        if (same && current.IsVerified) return new BindingStep.Reverify();
        return new BindingStep.Select(requested ?? retained, current.IsVerified && !same);
    }

    internal static ListingChoice ChooseFromListing(IReadOnlyList<TiaPortalProcessInfo> portals)
    {
        var projects = portals.Select(p => ProjectPathNormalization.Canonicalize(p.ProjectPath))
            .Where(p => p is not null).ToArray();
        return projects.Length switch
        {
            0 => new ListingChoice.NotFound(),
            1 => new ListingChoice.Select(projects[0]!),
            _ => new ListingChoice.Ambiguous()
        };
    }
}

public sealed record ProjectBindingOutcome(
    string Transition, ProjectBindingSnapshot Before, ProjectBindingSnapshot After,
    ProjectStatusInfo? Project, IReadOnlyList<TiaPortalProcessInfo> Portals,
    IReadOnlyList<string> Warnings, WorkerCallResult? Failure, bool IsRejection);
