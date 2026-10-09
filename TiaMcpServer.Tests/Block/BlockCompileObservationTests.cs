using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.OpennessWorker.Openness.Block;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class BlockCompileObservationTests
{
    [Fact]
    public void PreReportSelectionOrSessionFailureIsUnavailableWithoutReport()
    {
        var selection = BlockCompileObservation.Observe(new CompileObservationInput(
            report: null,
            targetSelectionAvailable: false,
            sessionAvailable: true,
            allInvocationsAvailable: false));
        var session = BlockCompileObservation.Observe(new CompileObservationInput(
            report: null,
            targetSelectionAvailable: true,
            sessionAvailable: false,
            allInvocationsAvailable: false));

        Assert.Equal("unavailable", selection.Stage);
        Assert.Null(selection.Report);
        Assert.Equal("unavailable", session.Stage);
        Assert.Null(session.Report);
    }

    [Theory]
    [InlineData("Success", 0, 0, "succeeded")]
    [InlineData("Warning", 0, 2, "succeeded")]
    [InlineData("Error", 1, 0, "failed")]
    public void AvailableInvocationsUseReportStateAndTotals(
        string overallState,
        int errors,
        int warnings,
        string expectedStage)
    {
        var report = Report(overallState, errors, warnings);

        var observation = BlockCompileObservation.Observe(new CompileObservationInput(
            report,
            targetSelectionAvailable: true,
            sessionAvailable: true,
            allInvocationsAvailable: true));

        Assert.Equal(expectedStage, observation.Stage);
        Assert.Same(report, observation.Report);
    }

    [Fact]
    public void UnavailableInvocationWinsOverErrorsAndRetainsPartialReport()
    {
        var report = Report("Error", errors: 3, warnings: 1);
        report.Plcs.Clear();
        report.Plcs.Add(new PlcCompileInfo
        {
            PlcName = "PLC A",
            DeviceName = "Device A",
            State = "Error",
            DiagnosticNotes = { "Compilation failed; compiler details are unavailable." }
        });
        report.Plcs.Add(new PlcCompileInfo
        {
            PlcName = "PLC B",
            DeviceName = "Device B",
            State = "Error",
            ErrorCount = 3,
            WarningCount = 1
        });

        var observation = BlockCompileObservation.Observe(new CompileObservationInput(
            report,
            targetSelectionAvailable: true,
            sessionAvailable: true,
            allInvocationsAvailable: false));

        Assert.Equal("unavailable", observation.Stage);
        Assert.Same(report, observation.Report);
        Assert.Equal(new[] { "PLC A", "PLC B" }, observation.Report!.Plcs.Select(plc => plc.PlcName));
        Assert.Equal(3, observation.Report.TotalErrorCount);
        Assert.Equal(1, observation.Report.TotalWarningCount);
    }

    [Fact]
    public void FromReportRequiresAReportAndRepresentsCleanCompile()
    {
        Assert.Throws<ArgumentNullException>(() => BlockCompileObservation.FromReport(null!));

        var observation = BlockCompileObservation.FromReport(Report("Success", 0, 0));

        Assert.Equal("succeeded", observation.Stage);
        Assert.NotNull(observation.Report);
    }

    [Fact]
    public void StructuralOmissionEvidenceIsCarriedWithoutParsingNotes()
    {
        var observation = BlockCompileObservation.Observe(new CompileObservationInput(
            Report("Warning", 0, 1),
            targetSelectionAvailable: true,
            sessionAvailable: true,
            allInvocationsAvailable: true,
            detailsOmitted: true));

        Assert.Equal("succeeded", observation.Stage);
        Assert.True(observation.DetailsOmitted);
    }

    private static CompileCheckReport Report(string state, int errors, int warnings) => new()
    {
        Scope = "plc",
        OverallState = state,
        TotalErrorCount = errors,
        TotalWarningCount = warnings,
        Plcs =
        {
            new PlcCompileInfo
            {
                PlcName = "PLC",
                DeviceName = "Device",
                State = state,
                ErrorCount = errors,
                WarningCount = warnings
            }
        }
    };
}
