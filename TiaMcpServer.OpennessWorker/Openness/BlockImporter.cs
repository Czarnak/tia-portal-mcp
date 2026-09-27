using System;
using System.Collections.Generic;
using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class BlockImporter
{
    internal static BlockImportResult Import(
        Project project,
        string blockPath,
        string yamlContent,
        string format)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        if (yamlContent is null) throw new ArgumentNullException(nameof(yamlContent));

        return BlockImportCoordinator.ExecuteWithPreTargetOutcome(
            () =>
            {
                if (!string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal))
                    return ImportSource(project, blockPath, yamlContent);

                var fallbackDocumentName = Path.GetFileName(blockPath) + ".xml";
                var preflight = BlockWritePreflight.PrepareUpdate(
                    blockPath,
                    fallbackDocumentName,
                    yamlContent);

                return BlockImportCoordinator.Execute(
                    fallbackDocumentName,
                    yamlContent,
                    (directory, bundle, boundary) => ImportDocuments(
                        project,
                        preflight.Address,
                        blockPath,
                        directory,
                        bundle,
                        boundary),
                    compileAllowed => ObservePostconditions(
                        project,
                        preflight.Address,
                        blockPath,
                        SourceFormatNames.Xml,
                        preflight.Bundle.PrimaryDocumentName,
                        compileAllowed,
                        warnings: null));
            },
            sourceApplicable: !string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal));
    }

    private static void ImportDocuments(
        Project project,
        BlockAddress address,
        string blockPath,
        DirectoryInfo directory,
        ParsedBlockImportBundle bundle,
        BlockImportInvocationBoundary boundary)
    {
        var target = BlockTargetResolver.ResolveForImport(project, address);

        if (BlockImportRouting.SelectRoute(bundle) == BlockImportRoute.SimaticMl)
        {
            var authoritative = BlockImportRouting.SelectAuthoritativeDocument(bundle);
            if (bundle.Documents.Count > 1)
            {
                var current = BlockImportBundleParser.Parse(
                    authoritative.LogicalName,
                    BlockExporter.Export(project, blockPath, SourceFormatNames.Xml));
                BlockImportRouting.EnsureOnlyAuthoritativeDocumentChanged(
                    bundle,
                    current,
                    authoritative.LogicalName);
            }

            var xmlPath = Path.Combine(directory.FullName, authoritative.SafeFileName);
            boundary.BeforeSiemensCall();
            target.Group.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);
            boundary.AfterSiemensCallReturned();
            boundary.RecordReturnedResult(BlockImportReturnedState.Success);
            return;
        }

        boundary.BeforeSiemensCall();
        var result = target.Group.Blocks.ImportFromDocuments(
            directory,
            BlockImportRouting.SimaticSdBaseName(bundle),
            ImportDocumentOptions.Override);
        boundary.AfterSiemensCallReturned();
        boundary.RecordReturnedResult(result.State == DocumentResultState.Success
            ? BlockImportReturnedState.Success
            : BlockImportReturnedState.NonSuccess);

        if (result.State != DocumentResultState.Success)
            throw new InvalidOperationException("SIMATIC SD import returned a non-success state.");
    }

    private static BlockImportResult ImportSource(
        Project project,
        string blockPath,
        string sourceContent)
    {
        var address = BlockWritePreflight.ParseAddress(blockPath);
        var target = BlockTargetResolver.ResolveForImport(project, address);
        if (target.Block is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"No block exists at '{address.ToDisplayPath()}'. update_block_logic only updates a "
                + "block that is already in the project; it never creates one.");
        }

        var decision = BlockExporter.DecideSourceFormat(target.Block, address);
        var targetName = target.Block.Name;
        if (!PlcTypeSourcePreflight.TryReadDeclaredName(
                sourceContent,
                SourceFormatNames.Source,
                decision.ExpectedKind,
                out var declaredName,
                out var preflightError))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                preflightError ?? "The submitted document declares no object name.");
        }

        if (!string.Equals(declaredName, targetName, StringComparison.Ordinal))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"The submitted document declares '{declaredName}' but '{address.ToDisplayPath()}' "
                + $"resolves to '{targetName}'. update_block_logic never renames and never creates: "
                + $"submit a document declaring '{targetName}', or address the block the document "
                + "actually declares.");
        }

        var warnings = new List<string>();
        return BlockImportCoordinator.ExecuteSource(
            (boundary, sourceTracker) =>
            {
                var scope = ExternalSourceScope.Create(
                    target.ExternalSourceGroup,
                    targetName + decision.Extension,
                    sourceContent,
                    sourceTracker);
                IList<IEngineeringObject>? generated = null;

                try
                {
                    boundary.BeforeSiemensCall();
                    if (target.UserGroup is not null)
                    {
                        generated = scope.Source.GenerateBlocksFromSource(target.UserGroup, GenerateBlockOption.None);
                    }
                    else
                    {
                        generated = scope.Source.GenerateBlocksFromSource(GenerateBlockOption.None);
                    }
                    boundary.AfterSiemensCallReturned();
                    boundary.RecordReturnedResult(BlockImportReturnedState.Success);
                }
                finally
                {
                    scope.Dispose();
                    if (!scope.ProjectNodeRemoved)
                        warnings.Add(BlockImportDiagnosticSanitizer.SourceNodeCleanupWarning());
                    if (generated is not null && generated.Count != 1)
                        warnings.Add(BlockImportDiagnosticSanitizer.GeneratedCountWarning(generated.Count));
                }
            },
            compileAllowed => ObservePostconditions(
                project,
                address,
                blockPath,
                SourceFormatNames.Source,
                primaryDocumentName: null,
                compileAllowed,
                warnings));
    }

    private static BlockPostconditionEvidence ObservePostconditions(
        Project project,
        BlockAddress address,
        string blockPath,
        string format,
        string? primaryDocumentName,
        bool compileAllowed,
        IReadOnlyList<string>? warnings)
    {
        var compile = compileAllowed
            ? BlockCompileObservation.Observe(CompileChecker.CompileObserved(
                project,
                string.Equals(format, SourceFormatNames.Source, StringComparison.Ordinal)
                    ? address.PlcName
                    : null,
                string.Equals(format, SourceFormatNames.Source, StringComparison.Ordinal)
                    ? null
                    : blockPath))
            : BlockCompileObservation.Unavailable(report: null);

        return ObserveFinalState(
            project,
            address,
            blockPath,
            format,
            primaryDocumentName,
            compile,
            warnings);
    }

    private static BlockPostconditionEvidence ObserveFinalState(
        Project project,
        BlockAddress address,
        string blockPath,
        string format,
        string? primaryDocumentName,
        BlockCompileObservation compile,
        IReadOnlyList<string>? warnings)
    {
        ResolvedBlockTarget target;
        try
        {
            target = BlockTargetResolver.ResolveForImport(project, address);
        }
        catch (Exception)
        {
            return BlockPostconditionEvidence.Import(
                compile,
                finalReadStage: "unavailable",
                targetPresent: null,
                warnings);
        }

        if (target.Block is null)
        {
            return BlockPostconditionEvidence.Import(
                compile,
                finalReadStage: "succeeded",
                targetPresent: false,
                warnings);
        }

        try
        {
            bool exportSucceeded;
            if (string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal))
            {
                var verification = BlockExporter.VerifyPrimaryDocument(
                    project,
                    blockPath,
                    primaryDocumentName
                        ?? throw new InvalidOperationException("Primary XML document is unavailable."));
                exportSucceeded = verification.ReExportSucceeded;
            }
            else
            {
                exportSucceeded = !string.IsNullOrWhiteSpace(
                    BlockExporter.Export(project, blockPath, SourceFormatNames.Source));
            }

            return BlockPostconditionEvidence.Import(
                compile,
                exportSucceeded ? "succeeded" : "unavailable",
                targetPresent: true,
                warnings);
        }
        catch (Exception)
        {
            return BlockPostconditionEvidence.Import(
                compile,
                finalReadStage: "unavailable",
                targetPresent: true,
                warnings);
        }
    }
}
