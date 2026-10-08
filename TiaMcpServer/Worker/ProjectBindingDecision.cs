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
    public sealed record Select(string? Path, bool IsSwitch, int? PortalProcessId = null) : BindingStep;
    public sealed record Reject(string Category, string Message) : BindingStep;
}

internal abstract record ListingChoice
{
    public sealed record Select(string? Path, int PortalProcessId) : ListingChoice;
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

    internal static BindingStep Decide(ProjectBindingSnapshot current, string? requestedPath, bool forceRebind,
        int? portalProcessId = null)
    {
        var requested = ProjectPathNormalization.Canonicalize(requestedPath);
        var retained = ProjectPathNormalization.Canonicalize(current.ProjectPath);
        if (current.State == ProjectBindingSnapshot.UnboundState)
            return requested is null ? new BindingStep.ListThenSelect() : new BindingStep.Select(requested, false, portalProcessId);
        if (current.State is not (ProjectBindingSnapshot.ConfiguredUnverifiedState
            or ProjectBindingSnapshot.InvalidatedState or ProjectBindingSnapshot.VerifiedState) || retained is null)
            return new BindingStep.Reject(WorkerFailureCategories.BindingConflict, "The session binding state cannot select an open project.");
        var same = (requested is null || string.Equals(requested, retained, StringComparison.OrdinalIgnoreCase))
            && (!current.IsVerified || portalProcessId is null || portalProcessId == current.PortalProcessId);
        if (!same && !forceRebind)
            return new BindingStep.Reject(WorkerFailureCategories.BindingConflict, "A different project requires forceRebind=true.");
        if (same && current.IsVerified) return new BindingStep.Reverify();
        return new BindingStep.Select(requested ?? retained, current.IsVerified && !same, portalProcessId);
    }

    internal static ListingChoice ChooseFromListing(IReadOnlyList<TiaPortalProcessInfo> portals,
        int? portalProcessId = null)
    {
        var candidates = portals.Where(p => portalProcessId is null
            ? ProjectPathNormalization.Canonicalize(p.ProjectPath) is not null
            : p.ProcessId == portalProcessId).ToArray();
        if (portalProcessId is null && candidates.Length == 0 && portals.Count == 1)
            candidates = portals.ToArray();
        if (candidates.Length == 1)
            return new ListingChoice.Select(portalProcessId is null
                ? ProjectPathNormalization.Canonicalize(candidates[0].ProjectPath) : null,
                candidates[0].ProcessId);
        return candidates.Length switch
        {
            0 when portalProcessId is null && portals.Count > 1 => new ListingChoice.Ambiguous(),
            0 => new ListingChoice.NotFound(),
            _ => new ListingChoice.Ambiguous()
        };
    }
}

public sealed record ProjectBindingOutcome(
    string Transition, ProjectBindingSnapshot Before, ProjectBindingSnapshot After,
    ProjectStatusInfo? Project, IReadOnlyList<TiaPortalProcessInfo> Portals,
    IReadOnlyList<string> Warnings, WorkerCallResult? Failure, bool IsRejection);
