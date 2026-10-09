using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class BlockPostconditionVerifierTests
{
    [Fact]
    public void VerifyImport_AcceptsWarningOnlyCompileAndPresentFinalRead()
    {
        var boundary = CompletedBoundary();
        var report = Report("Warning", errors: 0, warnings: 2);
        var evidence = BlockPostconditionEvidence.Import(
            BlockCompileObservation.FromReport(report),
            finalReadStage: "succeeded",
            targetPresent: true);

        var outcome = BlockPostconditionVerifier.VerifyImport(
            evidence,
            boundary.Snapshot(),
            temporarySourceState: "not_applicable");

        Assert.Equal("succeeded", outcome.CompileStage);
        Assert.Same(report, evidence.CompileObservation.Report);
        Assert.NotSame(report, outcome.CompileReport);
        Assert.Equal("succeeded", outcome.FinalReadStage);
        Assert.True(outcome.TargetPresent);
        Assert.Equal("unknown", outcome.ContentRelation);
    }

    [Fact]
    public void VerifyImport_UnavailableCompileMayCarryNoReportAndThrowsSameOutcome()
    {
        var evidence = BlockPostconditionEvidence.Import(
            BlockCompileObservation.Unavailable(report: null),
            finalReadStage: "succeeded",
            targetPresent: true);

        var exception = Assert.Throws<WorkerOperationException>(() =>
            BlockPostconditionVerifier.VerifyImport(
                evidence,
                CompletedBoundary().Snapshot(),
                temporarySourceState: "not_applicable"));

        Assert.Equal(WorkerFailureCategories.PostconditionFailed, exception.FailureCategory);
        Assert.NotNull(exception.BlockImportOutcome);
        Assert.Equal("unavailable", exception.BlockImportOutcome!.CompileStage);
        Assert.Null(exception.BlockImportOutcome.CompileReport);
        Assert.True(exception.BlockImportOutcome.TargetMutationCommitted);
    }

    [Fact]
    public void VerifyImport_ReliableAbsenceIsSucceededReadWithFalsePresence()
    {
        var exception = Assert.Throws<WorkerOperationException>(() =>
            BlockPostconditionVerifier.VerifyImport(
                BlockPostconditionEvidence.Import(
                    BlockCompileObservation.FromReport(Report("Success", 0, 0)),
                    finalReadStage: "succeeded",
                    targetPresent: false),
                CompletedBoundary().Snapshot(),
                temporarySourceState: "not_applicable"));

        Assert.Equal("succeeded", exception.BlockImportOutcome!.FinalReadStage);
        Assert.False(exception.BlockImportOutcome.TargetPresent);
        Assert.Equal("unknown", exception.BlockImportOutcome.ContentRelation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    public void VerifyImport_ReadFailureIsUnavailableAndPreservesOnlyReliablePresence(bool? targetPresent)
    {
        var exception = Assert.Throws<WorkerOperationException>(() =>
            BlockPostconditionVerifier.VerifyImport(
                BlockPostconditionEvidence.Import(
                    BlockCompileObservation.FromReport(Report("Success", 0, 0)),
                    finalReadStage: "unavailable",
                    targetPresent: targetPresent),
                CompletedBoundary().Snapshot(),
                temporarySourceState: "not_applicable"));

        Assert.Equal("unavailable", exception.BlockImportOutcome!.FinalReadStage);
        Assert.Equal(targetPresent, exception.BlockImportOutcome.TargetPresent);
        Assert.Equal("unknown", exception.BlockImportOutcome.ContentRelation);
    }

    [Fact]
    public void Verify_AcceptsSuccessfulCompileAndNonEmptyReExport()
    {
        BlockPostconditionVerifier.Verify(new BlockPostconditionEvidence(
            compileSucceeded: true,
            reExportSucceeded: true,
            diagnosticMessage: "Verified."));
    }

    [Fact]
    public void Verify_RejectsCompileFailureAsPostconditionFailed()
    {
        var exception = Assert.Throws<WorkerOperationException>(() =>
            BlockPostconditionVerifier.Verify(new BlockPostconditionEvidence(
                compileSucceeded: false,
                reExportSucceeded: true,
                diagnosticMessage: "Compilation reported errors.")));

        Assert.Equal(WorkerFailureCategories.PostconditionFailed, exception.FailureCategory);
        Assert.StartsWith("Block update postcondition failed:", exception.Message);
    }

    [Fact]
    public void Verify_RejectsMissingReExportAsPostconditionFailed()
    {
        var exception = Assert.Throws<WorkerOperationException>(() =>
            BlockPostconditionVerifier.Verify(new BlockPostconditionEvidence(
                compileSucceeded: true,
                reExportSucceeded: false,
                diagnosticMessage: "Re-exported primary document was missing.")));

        Assert.Equal(WorkerFailureCategories.PostconditionFailed, exception.FailureCategory);
    }

    [Fact]
    public void Verify_RedactsDiagnosticMessageFromCallerVisibleError()
    {
        const string rawDiagnostic = "RAW_INTERNAL_TIA_EXCEPTION: C:\\Users\\operator\\secret-project.ap21";

        var exception = Assert.Throws<WorkerOperationException>(() =>
            BlockPostconditionVerifier.Verify(new BlockPostconditionEvidence(
                compileSucceeded: true,
                reExportSucceeded: false,
                diagnosticMessage: rawDiagnostic)));

        Assert.Equal(WorkerFailureCategories.PostconditionFailed, exception.FailureCategory);
        Assert.Equal("Block update postcondition failed: verification did not complete.", exception.Message);
        Assert.DoesNotContain(rawDiagnostic, exception.Message, StringComparison.Ordinal);
        Assert.Contains(exception.Warnings, warning =>
            warning.Contains("project state may have changed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Verify_FailureCarriesUncertainStateWarning()
    {
        var exception = Assert.Throws<WorkerOperationException>(() =>
            BlockPostconditionVerifier.Verify(new BlockPostconditionEvidence(
                compileSucceeded: false,
                reExportSucceeded: false,
                diagnosticMessage: "Compilation failed.")));

        Assert.Contains(exception.Warnings, warning =>
            warning.Contains("project state may have changed", StringComparison.OrdinalIgnoreCase));
    }

    private static BlockImportInvocationBoundary CompletedBoundary()
    {
        var boundary = new BlockImportInvocationBoundary();
        boundary.BeforeSiemensCall();
        boundary.AfterSiemensCallReturned();
        boundary.RecordReturnedResult(BlockImportReturnedState.Success);
        return boundary;
    }

    private static CompileCheckReport Report(string state, int errors, int warnings) => new()
    {
        Scope = "block",
        BlockPath = "PLC/Blocks/Main",
        OverallState = state,
        TotalErrorCount = errors,
        TotalWarningCount = warnings,
        Plcs =
        {
            new PlcCompileInfo
            {
                PlcName = "PLC",
                State = state,
                ErrorCount = errors,
                WarningCount = warnings
            }
        }
    };

}
