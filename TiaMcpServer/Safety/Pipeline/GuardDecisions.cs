namespace TiaMcpServer.Safety.Pipeline;

/// <summary>What the pipeline does after evaluating guards.</summary>
public enum GuardDecisionKind
{
    /// <summary>No guard stops the call.</summary>
    Proceed,

    /// <summary>A block guard, or an unacknowledged acknowledge guard, stops the call.</summary>
    Blocked,

    /// <summary>The <c>acknowledge</c> list names a guard that did not fire.</summary>
    Invalid
}

/// <summary>The outcome of the acknowledge rule; <paramref name="Message"/> is null when proceeding.</summary>
public sealed record GuardDecision(
    GuardDecisionKind Kind, IReadOnlyList<WriteGuardReport> Guards, string? Message);

/// <summary>The acknowledge rule: which fired guards stop a write and which the caller may waive.</summary>
public static class GuardDecisions
{
    /// <summary>Returns null when the list is well-formed, otherwise the reason it is not.</summary>
    public static string? ValidateAcknowledgeList(IReadOnlyList<string>? acknowledge, WriteGuardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (acknowledge is null)
        {
            return null;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in acknowledge)
        {
            var error = ValidateAcknowledgeId(id, catalog);
            if (error is not null)
            {
                return error;
            }

            if (!seen.Add(id))
            {
                return $"Guard id '{id}' appears more than once in 'acknowledge'.";
            }
        }

        return null;
    }

    /// <summary>
    /// Decides after the planning pass: rejects acknowledgements for guards that did not fire,
    /// then applies the acknowledge rule. A dry run reports the same guards but never blocks.
    /// </summary>
    public static GuardDecision Decide(
        IReadOnlyList<FiredGuard> fired,
        IReadOnlyList<string>? acknowledge,
        WriteGuardCatalog catalog,
        bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(fired);
        ArgumentNullException.ThrowIfNull(catalog);

        var acked = ToSet(acknowledge);
        var reports = BuildReports(fired, acked, catalog);

        var notFired = acked.Where(id => fired.All(g => g.Id != id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (notFired.Count > 0)
        {
            return new GuardDecision(
                GuardDecisionKind.Invalid,
                reports,
                $"'acknowledge' lists guard(s) that did not fire for this call: {Join(notFired)}.");
        }

        var blocked = BlockedMessage(reports);
        return blocked is null || dryRun
            ? new GuardDecision(GuardDecisionKind.Proceed, reports, null)
            : new GuardDecision(GuardDecisionKind.Blocked, reports, blocked);
    }

    /// <summary>
    /// Decides for guards fired by a just-in-time re-plan. Acknowledgements for guards that did
    /// not fire are ignored: they may have belonged to another item.
    /// </summary>
    public static GuardDecision DecideLate(
        IReadOnlyList<FiredGuard> fired,
        IReadOnlyList<string>? acknowledge,
        WriteGuardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(fired);
        ArgumentNullException.ThrowIfNull(catalog);

        var reports = BuildReports(fired, ToSet(acknowledge), catalog);
        var blocked = BlockedMessage(reports);
        return blocked is null
            ? new GuardDecision(GuardDecisionKind.Proceed, reports, null)
            : new GuardDecision(GuardDecisionKind.Blocked, reports, blocked);
    }

    private static string? ValidateAcknowledgeId(string? id, WriteGuardCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "'acknowledge' must not contain a blank guard id.";
        }

        if (!catalog.TryGet(id, out var guard))
        {
            return $"'acknowledge' names unknown guard id '{id}'.";
        }

        return guard.Severity switch
        {
            WriteGuardSeverities.Info => $"Guard '{id}' is informational and cannot be acknowledged.",
            WriteGuardSeverities.Block => $"Guard '{id}' always blocks and cannot be acknowledged.",
            _ => null
        };
    }

    private static HashSet<string> ToSet(IReadOnlyList<string>? acknowledge)
        => new(acknowledge ?? Array.Empty<string>(), StringComparer.Ordinal);

    private static List<WriteGuardReport> BuildReports(
        IReadOnlyList<FiredGuard> fired, HashSet<string> acked, WriteGuardCatalog catalog)
    {
        var reports = new List<WriteGuardReport>(fired.Count);
        foreach (var guard in fired)
        {
            var severity = catalog.Get(guard.Id).Severity;
            bool? acknowledged = severity == WriteGuardSeverities.Acknowledge ? acked.Contains(guard.Id) : null;
            reports.Add(new WriteGuardReport(guard.Id, severity, guard.OperationId, guard.Message, acknowledged));
        }

        return reports;
    }

    private static string? BlockedMessage(IReadOnlyList<WriteGuardReport> reports)
    {
        var block = Ids(reports.Where(r => r.Severity == WriteGuardSeverities.Block));
        var unacked = Ids(reports.Where(r => r.Acknowledged == false));
        if (block.Count == 0 && unacked.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        if (block.Count > 0)
        {
            parts.Add($"blocked by guard(s) that cannot be acknowledged: {Join(block)}");
        }

        if (unacked.Count > 0)
        {
            parts.Add($"guard(s) need acknowledgement, list their ids in 'acknowledge': {Join(unacked)}");
        }

        return "The write was stopped: " + string.Join("; ", parts) + ".";
    }

    private static List<string> Ids(IEnumerable<WriteGuardReport> reports)
        => reports.Select(r => r.Id).Distinct(StringComparer.Ordinal).ToList();

    private static string Join(IEnumerable<string> ids) => string.Join(", ", ids.Select(id => $"'{id}'"));
}
