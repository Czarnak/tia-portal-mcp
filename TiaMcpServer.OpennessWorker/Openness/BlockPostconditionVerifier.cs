using System;
using System.Collections.Generic;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class BlockPostconditionVerifier
{
    private const string PublicFailureDetail = "verification did not complete.";
    private const string UncertainStateWarning =
        "Project state may have changed; inspect the project before retrying.";

    public static void Verify(BlockPostconditionEvidence evidence)
    {
        Verify(evidence, "update");
    }

    public static void Verify(BlockPostconditionEvidence evidence, string operation)
    {
        if (evidence is null) throw new ArgumentNullException(nameof(evidence));
        if (string.IsNullOrWhiteSpace(operation)) throw new ArgumentException("Operation is required.", nameof(operation));

        if (evidence.CompileSucceeded && evidence.ReExportSucceeded)
        {
            return;
        }

        throw new WorkerOperationException(
            WorkerFailureCategories.PostconditionFailed,
            "Block " + operation + " postcondition failed: " + PublicFailureDetail,
            new[] { UncertainStateWarning });
    }

    public static BlockImportOutcomeInfo VerifyImport(
        BlockPostconditionEvidence evidence,
        BlockImportInvocationSnapshot invocation,
        string temporarySourceState)
    {
        if (evidence is null) throw new ArgumentNullException(nameof(evidence));
        if (invocation is null) throw new ArgumentNullException(nameof(invocation));

        var outcome = CreateImportOutcome(evidence, invocation, temporarySourceState);
        if (outcome.ImportStage == "completed"
            && outcome.ImportResultState == "success"
            && outcome.CompileStage == "succeeded"
            && outcome.FinalReadStage == "succeeded"
            && outcome.TargetPresent == true)
        {
            return outcome;
        }

        var warnings = new List<string>(
            BlockImportDiagnosticSanitizer.SanitizeWarnings(evidence.Warnings));
        if (!warnings.Contains(UncertainStateWarning))
            warnings.Add(UncertainStateWarning);

        throw new WorkerOperationException(
            WorkerFailureCategories.PostconditionFailed,
            "Block update postcondition failed: " + PublicFailureDetail,
            warnings,
            outcome);
    }

    internal static BlockImportOutcomeInfo CreateImportOutcome(
        BlockPostconditionEvidence evidence,
        BlockImportInvocationSnapshot invocation,
        string temporarySourceState)
    {
        var compile = invocation.ImportStage == "completed"
            ? evidence.CompileObservation
            : invocation.ImportStage == "not_started"
                ? BlockCompileObservation.NotStarted()
                : BlockCompileObservation.Unavailable(evidence.CompileObservation.Report);
        var finalReadStage = invocation.ImportStage == "not_started"
            ? "not_started"
            : evidence.FinalReadStage;
        var targetPresent = finalReadStage == "not_started" ? null : evidence.TargetPresent;

        return BlockImportOutcomeProjection.Project(new BlockImportOutcomeInfo
        {
            ImportStage = invocation.ImportStage,
            ImportResultState = invocation.ImportResultState,
            TargetMutationCommitted = invocation.TargetMutationCommitted,
            CompileStage = compile.Stage,
            CompileReport = compile.Report,
            CompileDetailsOmitted = compile.DetailsOmitted,
            FinalReadStage = finalReadStage,
            TargetPresent = targetPresent,
            ContentRelation = "unknown",
            TemporarySourceState = temporarySourceState
        });
    }
}
