using System;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal sealed class CompileObservationInput
{
    public CompileObservationInput(
        CompileCheckReport? report,
        bool targetSelectionAvailable,
        bool sessionAvailable,
        bool allInvocationsAvailable,
        bool detailsOmitted = false)
    {
        Report = report;
        TargetSelectionAvailable = targetSelectionAvailable;
        SessionAvailable = sessionAvailable;
        AllInvocationsAvailable = allInvocationsAvailable;
        DetailsOmitted = detailsOmitted;
    }

    public CompileCheckReport? Report { get; }
    public bool TargetSelectionAvailable { get; }
    public bool SessionAvailable { get; }
    public bool AllInvocationsAvailable { get; }
    public bool DetailsOmitted { get; }
}

internal sealed class BlockCompileObservation
{
    private BlockCompileObservation(string stage, CompileCheckReport? report, bool detailsOmitted)
    {
        Stage = stage;
        Report = report;
        DetailsOmitted = detailsOmitted;
    }

    public string Stage { get; }
    public CompileCheckReport? Report { get; }
    public bool DetailsOmitted { get; }

    public static BlockCompileObservation Observe(CompileObservationInput input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        if (!input.TargetSelectionAvailable || !input.SessionAvailable || !input.AllInvocationsAvailable)
            return Unavailable(input.Report, input.DetailsOmitted);
        if (input.Report is null)
            return Unavailable(null, input.DetailsOmitted);

        var hasErrors = input.Report.TotalErrorCount > 0
            || string.Equals(input.Report.OverallState, "Error", StringComparison.OrdinalIgnoreCase);
        return new BlockCompileObservation(
            hasErrors ? "failed" : "succeeded",
            input.Report,
            input.DetailsOmitted);
    }

    public static BlockCompileObservation FromReport(CompileCheckReport report)
    {
        if (report is null) throw new ArgumentNullException(nameof(report));
        return Observe(new CompileObservationInput(
            report,
            targetSelectionAvailable: true,
            sessionAvailable: true,
            allInvocationsAvailable: true));
    }

    public static BlockCompileObservation Unavailable(
        CompileCheckReport? report,
        bool detailsOmitted = false) =>
        new BlockCompileObservation("unavailable", report, detailsOmitted);

    public static BlockCompileObservation NotStarted() =>
        new BlockCompileObservation("not_started", report: null, detailsOmitted: false);
}
