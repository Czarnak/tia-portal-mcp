using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests;

public class BlockImportCoordinatorTests
{
    [Fact]
    public void Execute_InvokesImportOnceAfterEveryStagedFileExists()
    {
        var importCalls = 0;
        var verificationCalls = 0;

        var result = BlockImportCoordinator.Execute(
            "fallback.xml",
            "--- FILE: Main.xml ---\n<Main />\n--- FILE: Types.xml ---\n<Types />",
            (directory, bundle) =>
            {
                importCalls++;
                Assert.Equal("Main.xml", bundle.PrimaryDocumentName);
                Assert.Equal("<Main />\n", File.ReadAllText(Path.Combine(directory.FullName, "Main.xml")));
                Assert.Equal("<Types />", File.ReadAllText(Path.Combine(directory.FullName, "Types.xml")));
            },
            () =>
            {
                verificationCalls++;
                return new BlockPostconditionEvidence(true, true, "Verified.");
            });

        Assert.Equal(1, importCalls);
        Assert.Equal(1, verificationCalls);
        Assert.Equal("Import succeeded.", result.Payload);
    }

    [Fact]
    public void Execute_InvalidBundle_DoesNotInvokeImport()
    {
        var importCalls = 0;

        var exception = Assert.Throws<WorkerOperationException>(() => BlockImportCoordinator.Execute(
            "fallback.xml",
            "--- FILE: ../escape.xml ---\n<Invalid />",
            (_, _) => importCalls++,
            () => new BlockPostconditionEvidence(true, true, "Verified.")));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
        Assert.Equal(0, importCalls);
    }

    [Fact]
    public void Execute_PostconditionFailure_DoesNotRetryImport()
    {
        var importCalls = 0;
        var verificationCalls = 0;

        var exception = Assert.Throws<WorkerOperationException>(() => BlockImportCoordinator.Execute(
            "Main.xml",
            "<Main />",
            (_, _) => importCalls++,
            () =>
            {
                verificationCalls++;
                return new BlockPostconditionEvidence(false, false, "Compile failed.");
            }));

        Assert.Equal(WorkerFailureCategories.PostconditionFailed, exception.FailureCategory);
        Assert.Equal(1, importCalls);
        Assert.Equal(1, verificationCalls);
    }

    [Fact]
    public void Execute_CleansStagingAfterSuccessAndFailure()
    {
        string? successfulStagingPath = null;
        BlockImportCoordinator.Execute(
            "Main.xml",
            "<Main />",
            (directory, _) => successfulStagingPath = directory.FullName,
            () => new BlockPostconditionEvidence(true, true, "Verified."));

        Assert.NotNull(successfulStagingPath);
        Assert.False(Directory.Exists(successfulStagingPath));

        string? failedStagingPath = null;
        var exception = Assert.Throws<WorkerOperationException>(() => BlockImportCoordinator.Execute(
            "Main.xml",
            "<Main />",
            (directory, _) =>
            {
                failedStagingPath = directory.FullName;
                throw new InvalidOperationException("Import failed.");
            },
            () => new BlockPostconditionEvidence(true, true, "Verified.")));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, exception.FailureCategory);
        Assert.NotNull(failedStagingPath);
        Assert.False(Directory.Exists(failedStagingPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_CleanupFailure_AddsCappedWarningWithoutChangingOutcome(bool importFails)
    {
        var cleanupFailure = new string('x', 600);

        if (importFails)
        {
            var exception = Assert.Throws<WorkerOperationException>(() => BlockImportCoordinator.Execute(
                "Main.xml",
                "<Main />",
                (_, _) => throw new InvalidOperationException("Import failed."),
                () => new BlockPostconditionEvidence(true, true, "Verified."),
                cleanupDirectory: _ => throw new IOException(cleanupFailure)));

            Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, exception.FailureCategory);
            var warning = Assert.Single(exception.Warnings);
            Assert.StartsWith("Block import staging cleanup failed: ", warning);
            Assert.Equal(512, warning.Length);
            return;
        }

        var result = BlockImportCoordinator.Execute(
            "Main.xml",
            "<Main />",
            (_, _) => { },
            () => new BlockPostconditionEvidence(true, true, "Verified."),
            cleanupDirectory: _ => throw new IOException(cleanupFailure));

        Assert.Equal("Import succeeded.", result.Payload);
        var successWarning = Assert.Single(result.Warnings);
        Assert.StartsWith("Block import staging cleanup failed: ", successWarning);
        Assert.Equal(512, successWarning.Length);
    }
}
