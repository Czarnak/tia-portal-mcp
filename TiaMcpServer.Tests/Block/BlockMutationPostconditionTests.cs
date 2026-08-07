using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class BlockMutationPostconditionTests
{
    [Fact]
    public void Execute_ImportsOnceAndReturnsSuccessAfterVerification()
    {
        var importCalls = 0;
        var verificationCalls = 0;

        var result = BlockCreationCoordinator.Execute(
            () =>
            {
                importCalls++;
                return "created";
            },
            () =>
            {
                verificationCalls++;
                return new BlockPostconditionEvidence(
                    compileSucceeded: true,
                    reExportSucceeded: true,
                    diagnosticMessage: "Created block resolved and compiled.");
            });

        Assert.Equal("created", result);
        Assert.Equal(1, importCalls);
        Assert.Equal(1, verificationCalls);
    }

    [Fact]
    public void Execute_CompileFailurePreventsSuccessAndDoesNotRetryImport()
    {
        var importCalls = 0;
        var verificationCalls = 0;

        var exception = Assert.Throws<WorkerOperationException>(() =>
            BlockCreationCoordinator.Execute(
                () =>
                {
                    importCalls++;
                    return "created";
                },
                () =>
                {
                    verificationCalls++;
                    return new BlockPostconditionEvidence(
                        compileSucceeded: false,
                        reExportSucceeded: true,
                        diagnosticMessage: "Compilation reported errors after block creation.");
                }));

        Assert.Equal(WorkerFailureCategories.PostconditionFailed, exception.FailureCategory);
        Assert.Equal(1, importCalls);
        Assert.Equal(1, verificationCalls);
    }

    [Fact]
    public void Execute_CompileFailureReportsCreatePostconditionFailure()
    {
        var exception = Assert.Throws<WorkerOperationException>(() =>
            BlockCreationCoordinator.Execute(
                () => "created",
                () => new BlockPostconditionEvidence(
                    compileSucceeded: false,
                    reExportSucceeded: true,
                    diagnosticMessage: "Compilation reported errors after block creation.")));

        Assert.StartsWith("Block create postcondition failed:", exception.Message);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, exception.FailureCategory);
        Assert.Contains(exception.Warnings, warning =>
            warning.Contains("project state may have changed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_ResolveFailurePreventsSuccessAndDoesNotRetryImport()
    {
        var importCalls = 0;
        var verificationCalls = 0;

        var exception = Assert.Throws<WorkerOperationException>(() =>
            BlockCreationCoordinator.Execute(
                () =>
                {
                    importCalls++;
                    return "created";
                },
                () =>
                {
                    verificationCalls++;
                    return new BlockPostconditionEvidence(
                        compileSucceeded: false,
                        reExportSucceeded: false,
                        diagnosticMessage: "Created block could not be resolved.");
                }));

        Assert.Equal(WorkerFailureCategories.PostconditionFailed, exception.FailureCategory);
        Assert.Contains(exception.Warnings, warning =>
            warning.Contains("project state may have changed", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, importCalls);
        Assert.Equal(1, verificationCalls);
    }
}
