namespace TiaMcpServer.Safety.Pipeline;

public enum GuardDecisionKind { Proceed, NeedsUser, Blocked }

public sealed record GuardDecision(GuardDecisionKind Kind, IReadOnlyList<WriteGuardReport> Guards, string? Message);

/// <summary>Block guards always stop a write; acknowledge guards follow the mode-derived policy.</summary>
public static class GuardDecisions
{
    public static GuardDecision Decide(IReadOnlyList<FiredGuard> fired, ConfirmationMode mode,
        WriteGuardCatalog catalog, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(fired);
        ArgumentNullException.ThrowIfNull(catalog);
        var reports = fired.Select(guard =>
        {
            var severity = catalog.Get(guard.Id).Severity;
            bool? acknowledged = severity == WriteGuardSeverities.Acknowledge ? mode == ConfirmationMode.Policy : null;
            return new WriteGuardReport(guard.Id, severity, guard.OperationId, guard.Message, acknowledged);
        }).ToArray();
        if (dryRun) return new(GuardDecisionKind.Proceed, reports, null);
        var blocks = reports.Where(guard => guard.Severity == WriteGuardSeverities.Block)
            .Select(guard => guard.Id).Distinct(StringComparer.Ordinal).ToArray();
        if (blocks.Length > 0)
            return new(GuardDecisionKind.Blocked, reports, "The write was stopped by block guard(s): " + string.Join(", ", blocks) + ".");
        return reports.Any(guard => guard.Acknowledged == false)
            ? new(GuardDecisionKind.NeedsUser, reports, "The write requires user confirmation for the planned consequences.")
            : new(GuardDecisionKind.Proceed, reports, null);
    }

    /// <summary>A late acknowledge guard cannot be covered by an earlier AskUser confirmation.</summary>
    public static GuardDecision DecideLate(IReadOnlyList<FiredGuard> fired, ConfirmationMode mode, WriteGuardCatalog catalog)
    {
        var decision = Decide(fired, mode, catalog, false);
        return decision.Kind == GuardDecisionKind.NeedsUser
            ? decision with { Kind = GuardDecisionKind.Blocked, Message = "The re-resolved operation requires confirmation for late consequences." }
            : decision;
    }
}
