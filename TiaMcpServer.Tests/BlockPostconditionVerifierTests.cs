using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests;

public class BlockPostconditionVerifierTests
{
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

}
