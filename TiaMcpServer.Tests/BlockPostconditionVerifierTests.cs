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
