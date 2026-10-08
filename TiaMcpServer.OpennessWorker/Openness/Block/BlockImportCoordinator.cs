using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class BlockImportCoordinator
{
    public static BlockImportResult Execute(
        string documentName,
        string rawContent,
        Action<DirectoryInfo, ParsedBlockImportBundle> importDocuments,
        Func<BlockPostconditionEvidence> verifyPostcondition,
        Action<string>? cleanupDirectory = null)
    {
        if (importDocuments is null) throw new ArgumentNullException(nameof(importDocuments));
        if (verifyPostcondition is null) throw new ArgumentNullException(nameof(verifyPostcondition));

        return Execute(
            documentName,
            rawContent,
            (directory, bundle, boundary) =>
            {
                boundary.BeforeSiemensCall();
                importDocuments(directory, bundle);
                boundary.AfterSiemensCallReturned();
                boundary.RecordReturnedResult(BlockImportReturnedState.Success);
            },
            compileAllowed => compileAllowed
                ? UpgradeLegacyEvidence(verifyPostcondition())
                : BlockPostconditionEvidence.Import(
                    BlockCompileObservation.Unavailable(null), "unavailable", null),
            cleanupDirectory);
    }

    public static BlockImportResult Execute(
        string documentName,
        string rawContent,
        Action<DirectoryInfo, ParsedBlockImportBundle, BlockImportInvocationBoundary> importDocuments,
        Func<bool, BlockPostconditionEvidence> observePostcondition,
        Action<string>? cleanupDirectory = null,
        Action<string>? createStagingDirectory = null,
        Func<string, ParsedBlockImportBundle, IReadOnlyList<string>>? stageDocuments = null)
    {
        if (importDocuments is null) throw new ArgumentNullException(nameof(importDocuments));
        if (observePostcondition is null) throw new ArgumentNullException(nameof(observePostcondition));

        ParsedBlockImportBundle bundle;
        try
        {
            bundle = BlockImportBundleParser.Parse(documentName, rawContent);
        }
        catch (Exception failure)
        {
            throw CreatePreTargetFailure(failure, sourceApplicable: false);
        }

        string stagingPath;
        try
        {
            stagingPath = Path.Combine(
                Path.GetTempPath(), "tia-mcp-import-" + Guid.NewGuid().ToString("N"));
        }
        catch (Exception failure)
        {
            throw CreatePreTargetFailure(failure, sourceApplicable: false);
        }

        try
        {
            if (createStagingDirectory is null)
                Directory.CreateDirectory(stagingPath);
            else
                createStagingDirectory(stagingPath);

            var stagedPaths = stageDocuments is null
                ? BlockImportStager.StageDocuments(stagingPath, bundle)
                : stageDocuments(stagingPath, bundle);
            VerifyStagedDocuments(stagingPath, bundle, stagedPaths);
        }
        catch (Exception failure)
        {
            var warnings = new List<string>();
            var cleanupWarning = TryCleanupStaging(stagingPath, cleanupDirectory);
            if (!string.IsNullOrEmpty(cleanupWarning))
                warnings.Add(cleanupWarning!);
            throw CreatePreTargetFailure(failure, sourceApplicable: false, warnings);
        }

        return RunOutcome(
            new BlockSourceArtifactTracker(sourceApplicable: false),
            (boundary, _) => importDocuments(new DirectoryInfo(stagingPath), bundle, boundary),
            observePostcondition,
            BlockImportDiagnosticContext.Import,
            () => TryCleanupStaging(stagingPath, cleanupDirectory));
    }

    public static BlockImportResult ExecuteSource(
        Action<BlockImportInvocationBoundary, BlockSourceArtifactTracker> importSource,
        Func<bool, BlockPostconditionEvidence> observePostcondition)
    {
        if (importSource is null) throw new ArgumentNullException(nameof(importSource));
        if (observePostcondition is null) throw new ArgumentNullException(nameof(observePostcondition));

        return RunOutcome(
            new BlockSourceArtifactTracker(sourceApplicable: true),
            importSource,
            observePostcondition,
            BlockImportDiagnosticContext.Generation,
            cleanup: null);
    }

    internal static T ExecuteWithPreTargetOutcome<T>(
        Func<T> operation,
        bool sourceApplicable)
    {
        if (operation is null) throw new ArgumentNullException(nameof(operation));

        try
        {
            return operation();
        }
        catch (WorkerOperationException failure) when (failure.BlockImportOutcome is null)
        {
            throw CreatePreTargetFailure(failure, sourceApplicable);
        }
        catch (Exception failure) when (failure is not WorkerOperationException)
        {
            throw CreatePreTargetFailure(failure, sourceApplicable);
        }
    }

    private static BlockImportResult RunOutcome(
        BlockSourceArtifactTracker sourceTracker,
        Action<BlockImportInvocationBoundary, BlockSourceArtifactTracker> invokeTarget,
        Func<bool, BlockPostconditionEvidence> observePostcondition,
        BlockImportDiagnosticContext failureContext,
        Func<string?>? cleanup)
    {
        var boundary = new BlockImportInvocationBoundary();
        var warnings = new List<string>();
        Exception? primaryFailure = null;

        try
        {
            invokeTarget(boundary, sourceTracker);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }

        BlockImportInvocationSnapshot invocation;
        try
        {
            invocation = boundary.Snapshot();
        }
        catch (Exception snapshotFailure)
        {
            throw CreateAmbiguousBoundaryFailure(
                primaryFailure ?? snapshotFailure,
                sourceTracker,
                observePostcondition,
                failureContext,
                cleanup);
        }

        BlockPostconditionEvidence evidence;
        if (invocation.ImportStage == "not_started")
        {
            evidence = BlockPostconditionEvidence.Import(
                BlockCompileObservation.NotStarted(), "not_started", null);
        }
        else
        {
            try
            {
                evidence = observePostcondition(invocation.ImportStage == "completed")
                    ?? throw new InvalidOperationException("Block update observation returned no evidence.");
            }
            catch (Exception observationFailure)
            {
                if (primaryFailure is null)
                    primaryFailure = observationFailure;
                warnings.Add(BlockImportDiagnosticSanitizer.Failure(
                    BlockImportDiagnosticContext.Verification, observationFailure));
                evidence = BlockPostconditionEvidence.Import(
                    BlockCompileObservation.Unavailable(null), "unavailable", null);
            }
        }

        AddWarnings(warnings, evidence.Warnings);
        if (cleanup is not null)
        {
            var cleanupWarning = cleanup();
            if (!string.IsNullOrEmpty(cleanupWarning))
                warnings.Add(cleanupWarning!);
        }
        warnings = new List<string>(BlockImportDiagnosticSanitizer.SanitizeWarnings(warnings));

        var outcome = BlockPostconditionVerifier.CreateImportOutcome(
            evidence,
            invocation,
            sourceTracker.Snapshot());

        if (primaryFailure is not null)
        {
            if (primaryFailure is WorkerOperationException workerFailure)
            {
                AddWarnings(warnings, workerFailure.Warnings);
                throw new WorkerOperationException(
                    workerFailure.FailureCategory,
                    BlockImportDiagnosticSanitizer.Failure(failureContext, workerFailure),
                    BlockImportDiagnosticSanitizer.SanitizeWarnings(warnings),
                    outcome);
            }

            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                BlockImportDiagnosticSanitizer.Failure(failureContext, primaryFailure),
                warnings,
                outcome);
        }

        try
        {
            outcome = BlockPostconditionVerifier.VerifyImport(
                evidence,
                invocation,
                sourceTracker.Snapshot());
        }
        catch (WorkerOperationException failure)
        {
            AddWarnings(warnings, failure.Warnings);
            throw new WorkerOperationException(
                failure.FailureCategory,
                failure.Message,
                BlockImportDiagnosticSanitizer.SanitizeWarnings(warnings),
                failure.BlockImportOutcome ?? outcome);
        }

        return new BlockImportResult(
            "Import succeeded.",
            outcome,
            BlockImportDiagnosticSanitizer.SanitizeWarnings(warnings));
    }

    private static WorkerOperationException CreateAmbiguousBoundaryFailure(
        Exception failure,
        BlockSourceArtifactTracker sourceTracker,
        Func<bool, BlockPostconditionEvidence> observePostcondition,
        BlockImportDiagnosticContext failureContext,
        Func<string?>? cleanup)
    {
        var warnings = new List<string>();
        if (failure is WorkerOperationException workerFailure)
            AddWarnings(warnings, workerFailure.Warnings);

        BlockPostconditionEvidence evidence;
        try
        {
            var observed = observePostcondition(false)
                ?? throw new InvalidOperationException("Block update observation returned no evidence.");
            evidence = BlockPostconditionEvidence.Import(
                BlockCompileObservation.Unavailable(report: null),
                observed.FinalReadStage,
                observed.TargetPresent,
                observed.Warnings);
        }
        catch (Exception observationFailure)
        {
            warnings.Add(BlockImportDiagnosticSanitizer.Failure(
                BlockImportDiagnosticContext.Verification, observationFailure));
            evidence = BlockPostconditionEvidence.Import(
                BlockCompileObservation.Unavailable(report: null), "unavailable", null);
        }

        AddWarnings(warnings, evidence.Warnings);
        if (cleanup is not null)
        {
            try
            {
                var cleanupWarning = cleanup();
                if (!string.IsNullOrEmpty(cleanupWarning))
                    warnings.Add(cleanupWarning!);
            }
            catch
            {
                warnings.Add(BlockImportDiagnosticSanitizer.StagingCleanupWarning());
            }
        }

        var outcome = BlockPostconditionVerifier.CreateImportOutcome(
            evidence,
            new BlockImportInvocationSnapshot("unknown", "unavailable", null),
            sourceTracker.Snapshot());

        return new WorkerOperationException(
            failure is WorkerOperationException categorized
                ? categorized.FailureCategory
                : WorkerFailureCategories.WorkerOperationFailed,
            BlockImportDiagnosticSanitizer.Failure(failureContext, failure),
            BlockImportDiagnosticSanitizer.SanitizeWarnings(warnings),
            outcome);
    }

    private static BlockPostconditionEvidence UpgradeLegacyEvidence(BlockPostconditionEvidence evidence)
    {
        if (evidence is null) throw new ArgumentNullException(nameof(evidence));
        var report = new CompileCheckReport
        {
            Scope = "block",
            OverallState = evidence.CompileSucceeded ? "Success" : "Error",
            TotalErrorCount = evidence.CompileSucceeded ? 0 : 1
        };
        return BlockPostconditionEvidence.Import(
            BlockCompileObservation.FromReport(report),
            evidence.ReExportSucceeded ? "succeeded" : "unavailable",
            evidence.ReExportSucceeded ? true : null,
            evidence.Warnings);
    }

    private static WorkerOperationException CreatePreTargetFailure(
        Exception failure,
        bool sourceApplicable,
        IReadOnlyList<string>? warnings = null)
    {
        var combinedWarnings = new List<string>();
        if (failure is WorkerOperationException workerFailure)
            AddWarnings(combinedWarnings, workerFailure.Warnings);
        if (warnings is not null)
            AddWarnings(combinedWarnings, warnings);

        var evidence = BlockPostconditionEvidence.Import(
            BlockCompileObservation.NotStarted(),
            finalReadStage: "not_started",
            targetPresent: null);
        var outcome = BlockPostconditionVerifier.CreateImportOutcome(
            evidence,
            new BlockImportInvocationBoundary().Snapshot(),
            new BlockSourceArtifactTracker(sourceApplicable).Snapshot());
        var context = sourceApplicable
            ? BlockImportDiagnosticContext.Generation
            : BlockImportDiagnosticContext.Import;

        return new WorkerOperationException(
            failure is WorkerOperationException categorized
                ? categorized.FailureCategory
                : WorkerFailureCategories.WorkerOperationFailed,
            BlockImportDiagnosticSanitizer.Failure(context, failure),
            BlockImportDiagnosticSanitizer.SanitizeWarnings(combinedWarnings),
            outcome);
    }

    private static string? TryCleanupStaging(
        string stagingPath,
        Action<string>? cleanupDirectory,
        List<string>? warnings = null)
    {
        try
        {
            if (!Directory.Exists(stagingPath))
                return null;
            (cleanupDirectory ?? (path => Directory.Delete(path, recursive: true)))(stagingPath);
            return null;
        }
        catch (Exception exception)
        {
            var warning = BlockImportDiagnosticSanitizer.StagingCleanupWarning();
            warnings?.Add(warning);
            _ = exception;
            return warning;
        }
    }

    private static void VerifyStagedDocuments(
        string stagingPath,
        ParsedBlockImportBundle bundle,
        IReadOnlyList<string> stagedPaths)
    {
        if (stagedPaths.Count != bundle.Documents.Count)
            throw new InvalidOperationException("Block import staging did not produce every declared document.");

        for (var index = 0; index < bundle.Documents.Count; index++)
        {
            var expectedPath = Path.GetFullPath(Path.Combine(stagingPath, bundle.Documents[index].SafeFileName));
            if (!string.Equals(expectedPath, stagedPaths[index], StringComparison.OrdinalIgnoreCase)
                || !File.Exists(stagedPaths[index]))
                throw new InvalidOperationException("Block import staging did not preserve the declared document order.");
        }
    }

    private static void AddWarnings(List<string> destination, IReadOnlyList<string> source)
    {
        foreach (var warning in source)
        {
            if (!destination.Contains(warning))
                destination.Add(warning);
        }
    }
}
