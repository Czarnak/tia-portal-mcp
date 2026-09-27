using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class BlockImportCoordinatorTests
{
    [Fact]
    public void InvocationBoundary_PreflightSnapshotIsNotStartedAndUncommitted()
    {
        var snapshot = new BlockImportInvocationBoundary().Snapshot();

        Assert.Equal("not_started", snapshot.ImportStage);
        Assert.Equal("unavailable", snapshot.ImportResultState);
        Assert.False(snapshot.TargetMutationCommitted);
    }

    [Fact]
    public void InvocationBoundary_StartedCallThatThrowsIsUnknown()
    {
        var boundary = new BlockImportInvocationBoundary();
        boundary.BeforeSiemensCall();

        var snapshot = boundary.Snapshot();

        Assert.Equal("unknown", snapshot.ImportStage);
        Assert.Equal("unavailable", snapshot.ImportResultState);
        Assert.Null(snapshot.TargetMutationCommitted);
    }

    [Theory]
    [InlineData("Success", "success")]
    [InlineData("NonSuccess", "non_success")]
    public void InvocationBoundary_NormalReturnRecordsClosedResult(
        string stateName,
        string expectedResult)
    {
        var state = Enum.Parse<BlockImportReturnedState>(stateName);
        var boundary = new BlockImportInvocationBoundary();
        boundary.BeforeSiemensCall();
        boundary.AfterSiemensCallReturned();
        boundary.RecordReturnedResult(state);

        var snapshot = boundary.Snapshot();

        Assert.Equal("completed", snapshot.ImportStage);
        Assert.Equal(expectedResult, snapshot.ImportResultState);
        Assert.True(snapshot.TargetMutationCommitted);
    }

    [Fact]
    public void InvocationBoundary_InvalidOrDuplicateMarkersFailClosed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new BlockImportInvocationBoundary().AfterSiemensCallReturned());
        Assert.Throws<InvalidOperationException>(() =>
            new BlockImportInvocationBoundary().RecordReturnedResult(BlockImportReturnedState.Success));

        var started = new BlockImportInvocationBoundary();
        started.BeforeSiemensCall();
        Assert.Throws<InvalidOperationException>(() => started.BeforeSiemensCall());

        var returned = new BlockImportInvocationBoundary();
        returned.BeforeSiemensCall();
        returned.AfterSiemensCallReturned();
        Assert.Throws<InvalidOperationException>(() => returned.AfterSiemensCallReturned());
        Assert.Throws<InvalidOperationException>(() => returned.Snapshot());

        returned.RecordReturnedResult(BlockImportReturnedState.Success);
        Assert.Throws<InvalidOperationException>(() =>
            returned.RecordReturnedResult(BlockImportReturnedState.Success));
    }

    [Theory]
    [InlineData(false, "not_applicable")]
    [InlineData(true, "not_created")]
    public void SourceArtifactTracker_InitialStateDistinguishesXmlAndSource(
        bool sourceApplicable,
        string expected)
    {
        Assert.Equal(expected, new BlockSourceArtifactTracker(sourceApplicable).Snapshot());
    }

    [Fact]
    public void SourceArtifactTracker_TracksCreationThrowAndDeleteOutcome()
    {
        var creationThrow = new BlockSourceArtifactTracker(sourceApplicable: true);
        creationThrow.BeforeCreateFromFile();
        Assert.Equal("unknown", creationThrow.Snapshot());

        var removed = new BlockSourceArtifactTracker(sourceApplicable: true);
        removed.BeforeCreateFromFile();
        removed.AfterCreateFromFileReturned();
        Assert.Equal("unknown", removed.Snapshot());
        removed.AfterDeleteAttempt(removed: true);
        Assert.Equal("removed", removed.Snapshot());

        var residue = new BlockSourceArtifactTracker(sourceApplicable: true);
        residue.BeforeCreateFromFile();
        residue.AfterCreateFromFileReturned();
        residue.AfterDeleteAttempt(removed: false);
        Assert.Equal("residue_possible", residue.Snapshot());
    }

    [Fact]
    public void SourceArtifactTracker_InvalidOrDuplicateMarkersFailClosed()
    {
        var tracker = new BlockSourceArtifactTracker(sourceApplicable: true);
        Assert.Throws<InvalidOperationException>(() => tracker.AfterCreateFromFileReturned());
        Assert.Throws<InvalidOperationException>(() => tracker.AfterDeleteAttempt(removed: true));

        tracker.BeforeCreateFromFile();
        Assert.Throws<InvalidOperationException>(() => tracker.BeforeCreateFromFile());
        tracker.AfterCreateFromFileReturned();
        Assert.Throws<InvalidOperationException>(() => tracker.AfterCreateFromFileReturned());
        tracker.AfterDeleteAttempt(removed: false);
        Assert.Throws<InvalidOperationException>(() => tracker.AfterDeleteAttempt(removed: true));
    }

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
    public void Execute_ParseFailureCarriesSanitizedNotStartedXmlOutcomeWithoutCleanupOrObservation()
    {
        var importCalls = 0;
        var observationCalls = 0;
        var cleanupCalls = 0;

        var exception = Assert.Throws<WorkerOperationException>(() => BlockImportCoordinator.Execute(
            "fallback.xml",
            "--- FILE: ../escape.xml ---\n<SECRET_CONTENT />",
            (_, _) => importCalls++,
            () =>
            {
                observationCalls++;
                return new BlockPostconditionEvidence(true, true, "Verified.");
            },
            cleanupDirectory: _ => cleanupCalls++));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
        Assert.Equal(BlockImportDiagnosticSanitizer.Failure(BlockImportDiagnosticContext.Import), exception.Message);
        Assert.Equal(0, importCalls);
        Assert.Equal(0, observationCalls);
        Assert.Equal(0, cleanupCalls);
        Assert.NotNull(exception.BlockImportOutcome);
        Assert.Equal("not_started", exception.BlockImportOutcome.ImportStage);
        Assert.Equal("unavailable", exception.BlockImportOutcome.ImportResultState);
        Assert.False(exception.BlockImportOutcome.TargetMutationCommitted);
        Assert.Equal("not_started", exception.BlockImportOutcome.CompileStage);
        Assert.Equal("not_started", exception.BlockImportOutcome.FinalReadStage);
        Assert.Equal("not_applicable", exception.BlockImportOutcome.TemporarySourceState);
        Assert.DoesNotContain("SECRET_CONTENT", exception.Message, StringComparison.Ordinal);
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
    public void Execute_StartedTargetThrowRunsOneFinalObservationWithoutCompileOrRetry()
    {
        var targetCalls = 0;
        var observations = 0;
        var compileRequests = 0;

        var exception = Assert.Throws<WorkerOperationException>(() => BlockImportCoordinator.Execute(
            "Main.xml",
            "<Main />",
            (_, _, boundary) =>
            {
                boundary.BeforeSiemensCall();
                targetCalls++;
                throw new InvalidOperationException("C:\\private\\Fixture.ap21");
            },
            compileAllowed =>
            {
                observations++;
                if (compileAllowed) compileRequests++;
                return BlockPostconditionEvidence.Import(
                    BlockCompileObservation.Unavailable(report: null),
                    finalReadStage: "unavailable",
                    targetPresent: null);
            }));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, exception.FailureCategory);
        Assert.Equal(1, targetCalls);
        Assert.Equal(1, observations);
        Assert.Equal(0, compileRequests);
        Assert.Equal("unknown", exception.BlockImportOutcome!.ImportStage);
        Assert.Null(exception.BlockImportOutcome.TargetMutationCommitted);
        Assert.Equal("unavailable", exception.BlockImportOutcome.FinalReadStage);
    }

    [Fact]
    public void Execute_ReturnedTargetWithoutResultMarkerRetainsObservedEvidenceAndCleansOnce()
    {
        var targetCalls = 0;
        var observationCalls = 0;
        var cleanupCalls = 0;
        bool? observedCompileAllowed = null;
        string? stagingPath = null;

        try
        {
            var exception = Assert.Throws<WorkerOperationException>(() => BlockImportCoordinator.Execute(
                "Main.xml",
                "<Main />",
                (directory, _, boundary) =>
                {
                    stagingPath = directory.FullName;
                    boundary.BeforeSiemensCall();
                    targetCalls++;
                    boundary.AfterSiemensCallReturned();
                },
                compileAllowed =>
                {
                    observationCalls++;
                    observedCompileAllowed = compileAllowed;
                    return BlockPostconditionEvidence.Import(
                        BlockCompileObservation.Unavailable(report: null),
                        finalReadStage: "succeeded",
                        targetPresent: true);
                },
                cleanupDirectory: path =>
                {
                    cleanupCalls++;
                    Directory.Delete(path, recursive: true);
                }));

            Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, exception.FailureCategory);
            Assert.Equal(BlockImportDiagnosticSanitizer.Failure(BlockImportDiagnosticContext.Import), exception.Message);
            Assert.Equal(1, targetCalls);
            Assert.Equal(1, observationCalls);
            Assert.False(observedCompileAllowed);
            Assert.Equal(1, cleanupCalls);
            Assert.NotNull(stagingPath);
            Assert.False(Directory.Exists(stagingPath));
            Assert.Empty(exception.Warnings);
            Assert.NotNull(exception.BlockImportOutcome);
            Assert.Equal("unknown", exception.BlockImportOutcome.ImportStage);
            Assert.Equal("unavailable", exception.BlockImportOutcome.ImportResultState);
            Assert.Null(exception.BlockImportOutcome.TargetMutationCommitted);
            Assert.Equal("unavailable", exception.BlockImportOutcome.CompileStage);
            Assert.Null(exception.BlockImportOutcome.CompileReport);
            Assert.Equal("succeeded", exception.BlockImportOutcome.FinalReadStage);
            Assert.True(exception.BlockImportOutcome.TargetPresent);
            Assert.Equal("not_applicable", exception.BlockImportOutcome.TemporarySourceState);
        }
        finally
        {
            if (stagingPath is not null && Directory.Exists(stagingPath))
                Directory.Delete(stagingPath, recursive: true);
        }
    }

    [Fact]
    public void Execute_SnapshotFailureCarriesClosedCleanupWarningWhenDeletionFails()
    {
        var cleanupCalls = 0;
        string? stagingPath = null;

        try
        {
            var exception = Assert.Throws<WorkerOperationException>(() => BlockImportCoordinator.Execute(
                "Main.xml",
                "<Main />",
                (directory, _, boundary) =>
                {
                    stagingPath = directory.FullName;
                    boundary.BeforeSiemensCall();
                    boundary.AfterSiemensCallReturned();
                },
                _ => BlockPostconditionEvidence.Import(
                    BlockCompileObservation.Unavailable(report: null),
                    finalReadStage: "succeeded",
                    targetPresent: false),
                cleanupDirectory: _ =>
                {
                    cleanupCalls++;
                    throw new IOException("C:\\private\\Fixture.ap21");
                }));

            Assert.Equal(1, cleanupCalls);
            Assert.Equal(BlockImportDiagnosticSanitizer.StagingCleanupWarning(), Assert.Single(exception.Warnings));
            Assert.DoesNotContain("Fixture.ap21", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Fixture.ap21", string.Join(" ", exception.Warnings), StringComparison.Ordinal);
            Assert.Equal("succeeded", exception.BlockImportOutcome!.FinalReadStage);
            Assert.False(exception.BlockImportOutcome.TargetPresent);
        }
        finally
        {
            if (stagingPath is not null && Directory.Exists(stagingPath))
                Directory.Delete(stagingPath, recursive: true);
        }
    }

    [Fact]
    public void ExecuteSource_SnapshotAndObservationFailureRetainsTruthfulSourceAndFallbackEvidence()
    {
        var observationCalls = 0;
        bool? observedCompileAllowed = null;

        var exception = Assert.Throws<WorkerOperationException>(() =>
            BlockImportCoordinator.ExecuteSource(
                (boundary, sourceTracker) =>
                {
                    sourceTracker.BeforeCreateFromFile();
                    sourceTracker.AfterCreateFromFileReturned();
                    sourceTracker.AfterDeleteAttempt(removed: true);
                    boundary.BeforeSiemensCall();
                    boundary.AfterSiemensCallReturned();
                },
                compileAllowed =>
                {
                    observationCalls++;
                    observedCompileAllowed = compileAllowed;
                    throw new IOException("Project/PLC/SecretNode C:\\private\\Fixture.ap21");
                }));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, exception.FailureCategory);
        Assert.Equal(
            BlockImportDiagnosticSanitizer.Failure(BlockImportDiagnosticContext.Generation),
            exception.Message);
        Assert.Equal(1, observationCalls);
        Assert.False(observedCompileAllowed);
        Assert.Equal(
            "Block import reported additional sanitized diagnostic information.",
            Assert.Single(exception.Warnings));
        Assert.DoesNotContain("SecretNode", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Fixture.ap21", string.Join(" ", exception.Warnings), StringComparison.Ordinal);
        Assert.NotNull(exception.BlockImportOutcome);
        Assert.Equal("unknown", exception.BlockImportOutcome.ImportStage);
        Assert.Equal("unavailable", exception.BlockImportOutcome.ImportResultState);
        Assert.Null(exception.BlockImportOutcome.TargetMutationCommitted);
        Assert.Equal("unavailable", exception.BlockImportOutcome.CompileStage);
        Assert.Null(exception.BlockImportOutcome.CompileReport);
        Assert.Equal("unavailable", exception.BlockImportOutcome.FinalReadStage);
        Assert.Null(exception.BlockImportOutcome.TargetPresent);
        Assert.Equal("removed", exception.BlockImportOutcome.TemporarySourceState);
    }

    [Fact]
    public void Execute_NormalTargetReturnCompilesAndObservesFinalStateOnce()
    {
        var targetCalls = 0;
        var observations = 0;

        var result = BlockImportCoordinator.Execute(
            "Main.xml",
            "<Main />",
            (_, _, boundary) =>
            {
                boundary.BeforeSiemensCall();
                targetCalls++;
                boundary.AfterSiemensCallReturned();
                boundary.RecordReturnedResult(BlockImportReturnedState.Success);
            },
            compileAllowed =>
            {
                observations++;
                Assert.True(compileAllowed);
                return BlockPostconditionEvidence.Import(
                    BlockCompileObservation.FromReport(CleanReport()),
                    finalReadStage: "succeeded",
                    targetPresent: true);
            });

        Assert.Equal(1, targetCalls);
        Assert.Equal(1, observations);
        Assert.Equal("completed", result.Outcome.ImportStage);
        Assert.Equal("success", result.Outcome.ImportResultState);
        Assert.True(result.Outcome.TargetMutationCommitted);
        Assert.Equal("not_applicable", result.Outcome.TemporarySourceState);
    }

    [Fact]
    public void ExecuteSource_FailureBeforeCreateMarkerIsNotCreated()
    {
        var exception = Assert.Throws<WorkerOperationException>(() =>
            BlockImportCoordinator.ExecuteSource(
                (_, _) => throw new WorkerOperationException(
                    WorkerFailureCategories.WorkerOperationFailed,
                    BlockImportDiagnosticSanitizer.Failure(
                        BlockImportDiagnosticContext.SourceNodeCreation)),
                _ => throw new InvalidOperationException("Observation must not run.")));

        Assert.Equal("not_started", exception.BlockImportOutcome!.ImportStage);
        Assert.False(exception.BlockImportOutcome.TargetMutationCommitted);
        Assert.Equal("not_created", exception.BlockImportOutcome.TemporarySourceState);
    }

    [Fact]
    public void ExecuteWithPreTargetOutcome_SourcePreflightPreservesCategoryAndSanitizesEvidence()
    {
        var exception = Assert.Throws<WorkerOperationException>(() =>
            BlockImportCoordinator.ExecuteWithPreTargetOutcome<BlockImportResult>(
                () => throw new WorkerOperationException(
                    WorkerFailureCategories.ValidationError,
                    "Project/PLC/SecretNode submitted-snippet",
                    new[] { "C:\\private\\Fixture.ap21" }),
                sourceApplicable: true));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
        Assert.Equal(
            BlockImportDiagnosticSanitizer.Failure(BlockImportDiagnosticContext.Generation),
            exception.Message);
        Assert.DoesNotContain("SecretNode", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Fixture.ap21", string.Join(" ", exception.Warnings), StringComparison.Ordinal);
        Assert.NotNull(exception.BlockImportOutcome);
        Assert.Equal("not_started", exception.BlockImportOutcome.ImportStage);
        Assert.Equal("unavailable", exception.BlockImportOutcome.ImportResultState);
        Assert.False(exception.BlockImportOutcome.TargetMutationCommitted);
        Assert.Equal("not_started", exception.BlockImportOutcome.CompileStage);
        Assert.Equal("not_started", exception.BlockImportOutcome.FinalReadStage);
        Assert.Equal("not_created", exception.BlockImportOutcome.TemporarySourceState);
    }

    [Fact]
    public void Execute_DirectoryCreationFailureIsSanitizedWithoutCleanupTargetOrObservation()
    {
        var targetCalls = 0;
        var observationCalls = 0;
        var cleanupCalls = 0;

        var exception = Assert.Throws<WorkerOperationException>(() => BlockImportCoordinator.Execute(
            "Main.xml",
            "<Main />",
            (_, _, _) => targetCalls++,
            _ =>
            {
                observationCalls++;
                return BlockPostconditionEvidence.Import(
                    BlockCompileObservation.NotStarted(), "not_started", null);
            },
            cleanupDirectory: _ => cleanupCalls++,
            createStagingDirectory: _ => throw new IOException("C:\\private\\Fixture.ap21")));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, exception.FailureCategory);
        Assert.Equal(BlockImportDiagnosticSanitizer.Failure(BlockImportDiagnosticContext.Import), exception.Message);
        Assert.Equal(0, targetCalls);
        Assert.Equal(0, observationCalls);
        Assert.Equal(0, cleanupCalls);
        AssertNotStartedXml(exception);
    }

    [Fact]
    public void Execute_StagingFailurePreservesCategoryAndSanitizedCleanupWarning()
    {
        var targetCalls = 0;
        var observationCalls = 0;

        var exception = Assert.Throws<WorkerOperationException>(() => BlockImportCoordinator.Execute(
            "Main.xml",
            "<Main />",
            (_, _, _) => targetCalls++,
            _ =>
            {
                observationCalls++;
                return BlockPostconditionEvidence.Import(
                    BlockCompileObservation.NotStarted(), "not_started", null);
            },
            cleanupDirectory: path =>
            {
                Directory.Delete(path, recursive: true);
                throw new IOException("\\\\server\\share\\Fixture.ap21");
            },
            stageDocuments: (_, _) => throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "submitted-snippet")));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
        Assert.Equal(BlockImportDiagnosticSanitizer.Failure(BlockImportDiagnosticContext.Import), exception.Message);
        Assert.Equal(0, targetCalls);
        Assert.Equal(0, observationCalls);
        Assert.Equal(BlockImportDiagnosticSanitizer.StagingCleanupWarning(), Assert.Single(exception.Warnings));
        AssertNotStartedXml(exception);
    }

    [Fact]
    public void Execute_StagedDocumentVerificationFailureIsSanitizedBeforeTarget()
    {
        var targetCalls = 0;
        var observationCalls = 0;
        var cleanupCalls = 0;

        var exception = Assert.Throws<WorkerOperationException>(() => BlockImportCoordinator.Execute(
            "Main.xml",
            "<Main />",
            (_, _, _) => targetCalls++,
            _ =>
            {
                observationCalls++;
                return BlockPostconditionEvidence.Import(
                    BlockCompileObservation.NotStarted(), "not_started", null);
            },
            cleanupDirectory: path =>
            {
                cleanupCalls++;
                Directory.Delete(path, recursive: true);
            },
            stageDocuments: (_, _) => Array.Empty<string>()));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, exception.FailureCategory);
        Assert.Equal(BlockImportDiagnosticSanitizer.Failure(BlockImportDiagnosticContext.Import), exception.Message);
        Assert.Equal(0, targetCalls);
        Assert.Equal(0, observationCalls);
        Assert.Equal(1, cleanupCalls);
        Assert.Empty(exception.Warnings);
        AssertNotStartedXml(exception);
    }

    [Fact]
    public void ExecuteSource_CreateFromFileThrowIsUnknown()
    {
        var exception = Assert.Throws<WorkerOperationException>(() =>
            BlockImportCoordinator.ExecuteSource(
                (_, source) =>
                {
                    source.BeforeCreateFromFile();
                    throw new WorkerOperationException(
                        WorkerFailureCategories.WorkerOperationFailed,
                        BlockImportDiagnosticSanitizer.Failure(
                            BlockImportDiagnosticContext.SourceNodeCreation));
                },
                _ => throw new InvalidOperationException("Observation must not run.")));

        Assert.Equal("not_started", exception.BlockImportOutcome!.ImportStage);
        Assert.Equal("unknown", exception.BlockImportOutcome.TemporarySourceState);
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
            Assert.Equal(BlockImportDiagnosticSanitizer.StagingCleanupWarning(), warning);
            Assert.DoesNotContain(cleanupFailure, warning, StringComparison.Ordinal);
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
        Assert.Equal(BlockImportDiagnosticSanitizer.StagingCleanupWarning(), successWarning);
        Assert.DoesNotContain(cleanupFailure, successWarning, StringComparison.Ordinal);
    }

    private static CompileCheckReport CleanReport() => new()
    {
        Scope = "block",
        BlockPath = "PLC/Blocks/Main",
        OverallState = "Success",
        Plcs =
        {
            new PlcCompileInfo { PlcName = "PLC", State = "Success" }
        }
    };

    private static void AssertNotStartedXml(WorkerOperationException exception)
    {
        Assert.NotNull(exception.BlockImportOutcome);
        Assert.Equal("not_started", exception.BlockImportOutcome.ImportStage);
        Assert.Equal("unavailable", exception.BlockImportOutcome.ImportResultState);
        Assert.False(exception.BlockImportOutcome.TargetMutationCommitted);
        Assert.Equal("not_started", exception.BlockImportOutcome.CompileStage);
        Assert.Equal("not_started", exception.BlockImportOutcome.FinalReadStage);
        Assert.Equal("not_applicable", exception.BlockImportOutcome.TemporarySourceState);
    }
}
