using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.ExternalSources;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

public static class BlockImporter
{
    internal static BlockImportResult Import(
        ProjectBase project,
        string blockPath,
        string yamlContent,
        string format,
        string? expectedContentHash = null)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        if (yamlContent is null) throw new ArgumentNullException(nameof(yamlContent));

        if (!string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal))
            return ImportSource(project, blockPath, yamlContent, expectedContentHash);

        var preflight = BlockImportCoordinator.ExecuteWithPreTargetOutcome(
            () =>
            {
                var fallbackDocumentName = Path.GetFileName(blockPath) + ".xml";
                var prepared = BlockWritePreflight.PrepareUpdate(
                    blockPath,
                    fallbackDocumentName,
                    yamlContent);

                // Update-only, and only against the document the write was planned on. Both refusals
                // come before any import, so the outcome reports the import as not started.
                var existing = BlockTargetResolver.ResolveForImport(project, prepared.Address);
                if (existing.Block is null)
                {
                    throw new WorkerOperationException(
                        WorkerFailureCategories.TargetNotFound,
                        $"No block exists at '{prepared.Address.ToDisplayPath()}'. update_block_logic only updates a "
                        + "block that is already in the project; it never creates one.");
                }

                PlcWritePreconditions.RequireContentHash(
                    expectedContentHash,
                    SourceFormatNames.Xml,
                    BlockExporter.Export(project, blockPath, SourceFormatNames.Xml));
                return new XmlImportPreflight(fallbackDocumentName, prepared);
            },
            sourceApplicable: false);

        return BlockImportCoordinator.Execute(
            preflight.FallbackDocumentName,
            yamlContent,
            (directory, bundle, boundary) => ImportDocuments(
                project,
                preflight.Prepared.Address,
                blockPath,
                directory,
                bundle,
                boundary),
            compileAllowed => ObservePostconditions(
                project,
                preflight.Prepared.Address,
                blockPath,
                SourceFormatNames.Xml,
                preflight.Prepared.Bundle.PrimaryDocumentName,
                compileAllowed,
                warnings: null));
    }

    private static void ImportDocuments(
        ProjectBase project,
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
        ProjectBase project,
        string blockPath,
        string sourceContent,
        string? expectedContentHash)
    {
        var preflight = BlockImportCoordinator.ExecuteWithPreTargetOutcome(
            () =>
            {
                var address = BlockWritePreflight.ParseAddress(blockPath);
                var target = BlockTargetResolver.ResolveForImport(project, address);
                if (target.Block is null)
                {
                    throw new WorkerOperationException(
                        WorkerFailureCategories.TargetNotFound,
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

                PlcWritePreconditions.RequireContentHash(
                    expectedContentHash,
                    SourceFormatNames.Source,
                    BlockExporter.Export(project, blockPath, SourceFormatNames.Source));

                return new SourceImportPreflight(address, target, decision, targetName);
            },
            sourceApplicable: true);

        var warnings = new List<string>();
        return BlockImportCoordinator.ExecuteSource(
            (boundary, sourceTracker) =>
            {
                var scope = ExternalSourceScope.Create(
                    preflight.Target.ExternalSourceGroup,
                    preflight.TargetName + preflight.Decision.Extension,
                    sourceContent,
                    sourceTracker);
                IList<IEngineeringObject>? generated = null;

                try
                {
                    boundary.BeforeSiemensCall();
                    if (preflight.Target.UserGroup is not null)
                    {
                        generated = scope.Source.GenerateBlocksFromSource(preflight.Target.UserGroup, GenerateBlockOption.None);
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
                preflight.Address,
                blockPath,
                SourceFormatNames.Source,
                primaryDocumentName: null,
                compileAllowed,
                warnings));
    }

    private sealed class XmlImportPreflight
    {
        public XmlImportPreflight(string fallbackDocumentName, BlockUpdatePreflight prepared)
        {
            FallbackDocumentName = fallbackDocumentName;
            Prepared = prepared;
        }

        public string FallbackDocumentName { get; }
        public BlockUpdatePreflight Prepared { get; }
    }

    private sealed class SourceImportPreflight
    {
        public SourceImportPreflight(
            BlockAddress address,
            ResolvedBlockTarget target,
            SourceFormatDecision decision,
            string targetName)
        {
            Address = address;
            Target = target;
            Decision = decision;
            TargetName = targetName;
        }

        public BlockAddress Address { get; }
        public ResolvedBlockTarget Target { get; }
        public SourceFormatDecision Decision { get; }
        public string TargetName { get; }
    }

    private static BlockPostconditionEvidence ObservePostconditions(
        ProjectBase project,
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
        ProjectBase project,
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
