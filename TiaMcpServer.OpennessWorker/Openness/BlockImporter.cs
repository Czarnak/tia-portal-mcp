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
    /// <param name="format">
    /// <see cref="SourceFormatNames.Xml"/> (the block default) takes the Simatic ML bundle route
    /// this operation has always taken. <see cref="SourceFormatNames.Source"/> takes the Siemens
    /// external-source route and is only available for a global data block. The host normalizes
    /// this to exactly one of the two before it reaches the worker.
    /// </param>
    internal static BlockImportResult Import(
        Project project,
        string blockPath,
        string yamlContent,
        string format)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        if (yamlContent is null) throw new ArgumentNullException(nameof(yamlContent));

        if (!string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal))
        {
            return ImportSource(project, blockPath, yamlContent);
        }

        var fallbackDocumentName = Path.GetFileName(blockPath) + ".xml";
        var preflight = BlockWritePreflight.PrepareUpdate(
            blockPath,
            fallbackDocumentName,
            yamlContent);

        return BlockImportCoordinator.Execute(
            fallbackDocumentName,
            yamlContent,
            (directory, bundle) => ImportDocuments(
                project,
                preflight.Address,
                blockPath,
                directory,
                bundle),
            () => VerifyPostconditions(
                project,
                blockPath,
                preflight.Bundle.PrimaryDocumentName));
    }

    private static void ImportDocuments(
        Project project,
        BlockAddress address,
        string blockPath,
        DirectoryInfo directory,
        ParsedBlockImportBundle bundle)
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
                    bundle, current, authoritative.LogicalName);
            }

            // A single Simatic ML XML document must go through Import(FileInfo, ImportOptions).
            // ImportFromDocuments is only for SIMATIC SD packages keyed by an extension-less
            // base name; passing it a bare .xml produces a misleading "file does not exist".
            var xmlPath = Path.Combine(directory.FullName, authoritative.SafeFileName);
            target.Group.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);
            return;
        }

        var result = target.Group.Blocks.ImportFromDocuments(
            directory,
            BlockImportRouting.SimaticSdBaseName(bundle),
            ImportDocumentOptions.Override);

        if (result.State != DocumentResultState.Success)
        {
            throw new InvalidOperationException("Import failed with state: " + result.State);
        }
    }

    private static BlockPostconditionEvidence VerifyPostconditions(
        Project project,
        string blockPath,
        string primaryDocumentName)
    {
        var compileFailure = CompileAndBuildFailureEvidence(
            project, plcName: null, blockPath, "block import", warnings: null);

        if (compileFailure is not null)
        {
            return compileFailure;
        }

        return BlockExporter.VerifyPrimaryDocument(project, blockPath, primaryDocumentName);
    }

    /// <summary>
    /// Compiles and returns failure evidence, or null when the compile is clean and the caller
    /// should go on to its own re-export check.
    ///
    /// <para>
    /// Shared by both write routes on purpose: the predicate below is this repo's definition of
    /// "the write broke the project", and a route that drifted from it would start accepting writes
    /// the other route rejects. The routes differ only in compile scope — the Simatic ML route
    /// compiles the one block, the external-source route compiles the whole PLC — and in the phrase
    /// that names the operation in the diagnostic.
    /// </para>
    /// </summary>
    /// <param name="messageSuffix">
    /// Names the operation inside "Compilation reported errors after {suffix}." and
    /// "Compilation could not complete after {suffix}: …".
    /// </param>
    private static BlockPostconditionEvidence? CompileAndBuildFailureEvidence(
        Project project,
        string? plcName,
        string? blockPath,
        string messageSuffix,
        IReadOnlyList<string>? warnings)
    {
        try
        {
            var report = CompileChecker.Compile(project, plcName, blockPath);
            if (report.TotalErrorCount != 0
                || string.Equals(report.OverallState, "Error", StringComparison.OrdinalIgnoreCase))
            {
                return new BlockPostconditionEvidence(
                    compileSucceeded: false,
                    reExportSucceeded: false,
                    diagnosticMessage: $"Compilation reported errors after {messageSuffix}.",
                    warnings: warnings);
            }
        }
        catch (Exception exception)
        {
            return new BlockPostconditionEvidence(
                compileSucceeded: false,
                reExportSucceeded: false,
                diagnosticMessage: $"Compilation could not complete after {messageSuffix}: " + exception.Message,
                warnings: warnings);
        }

        return null;
    }

    /// <summary>
    /// Updates one existing global data block from Siemens external-source text (.db), mirroring
    /// <see cref="PlcTypeImporter"/>.
    ///
    /// <para>
    /// Strictly an update. Three refusals come before any project mutation: the block must already
    /// exist, it must be a global data block, and the name the submitted document declares must
    /// match the block the path resolved to. Openness' GenerateBlocksFromSource has no notion of a
    /// target object — it creates whatever the source declares — so without those refusals a typo
    /// in the path would silently add a stray block instead of failing.
    /// </para>
    /// <para>
    /// It does not go through <see cref="BlockImportCoordinator"/>: that coordinator exists to
    /// stage a multi-document Simatic ML bundle onto disk and verify the staged files, and an
    /// external source is a single document whose temp file and project node are owned by
    /// <see cref="ExternalSourceScope"/> instead.
    /// </para>
    /// </summary>
    private static BlockImportResult ImportSource(Project project, string blockPath, string sourceContent)
    {
        // 1. Parse the path and resolve the target. Same defensive parse as the Simatic ML route,
        // so a malformed path is a validation error rather than an uncategorized failure.
        var address = BlockWritePreflight.ParseAddress(blockPath);
        var target = BlockTargetResolver.ResolveForImport(project, address);

        // 2/3. Refuse if the block does not exist, or is not a global data block. This is an
        // update, never an upsert.
        if (target.Block is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"No block exists at '{address.ToDisplayPath()}'. update_block_logic only updates a "
                + "block that is already in the project; it never creates one.");
        }

        BlockExporter.RequireGlobalDb(target.Block, address);
        var targetName = target.Block.Name;

        // 4. Refuse if the submitted document declares a different object.
        if (!PlcTypeSourcePreflight.TryReadDeclaredName(
                sourceContent, SourceFormatNames.Source, out var declaredName, out var preflightError))
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
                + $"resolves to the data block '{targetName}'. update_block_logic never renames and "
                + $"never creates: submit a document declaring '{targetName}', or address the block "
                + "the document actually declares.");
        }

        // 5. Apply the document through the Siemens external-source pipeline.
        //
        // target.ExternalSourceGroup, not one re-derived from the block: for a block inside a
        // software unit this is the unit's own group, and registering under the top-level PLC
        // instead would generate a stray block there and leave the real one untouched.
        var scope = ExternalSourceScope.Create(
            target.ExternalSourceGroup, targetName + ".db", sourceContent);

        IList<IEngineeringObject>? generated;

        try
        {
            if (target.UserGroup is not null)
            {
                generated = scope.Source.GenerateBlocksFromSource(target.UserGroup, GenerateBlockOption.None);
            }
            else
            {
                // The Program blocks root is a PlcBlockSystemGroup, not a user group, so there is
                // no group to pass. GenerateBlockOption.None is still passed explicitly: the truly
                // parameterless overload leaves the on-error behaviour implicit, and both branches
                // must refuse to keep partially generated blocks.
                generated = scope.Source.GenerateBlocksFromSource(GenerateBlockOption.None);
            }
        }
        finally
        {
            scope.Dispose();
        }

        // 6. Verify. The warnings seeded here are the two things verification itself cannot see.
        var warnings = BuildSourceWriteWarnings(
            address, scope.ProjectNodeRemoved, generated?.Count ?? 0);

        var evidence = VerifySourcePostconditions(project, address, blockPath, warnings);

        try
        {
            BlockPostconditionVerifier.Verify(evidence);
        }
        catch (WorkerOperationException failure)
        {
            // The shared verifier does not know about the evidence's own warnings, and a residual
            // external source node is exactly the kind of unrequested project change a failed write
            // must still report.
            var failureWarnings = new List<string>(evidence.Warnings);
            failureWarnings.AddRange(failure.Warnings);

            throw new WorkerOperationException(failure.FailureCategory, failure.Message, failureWarnings);
        }

        return new BlockImportResult("Import succeeded.", evidence.Warnings);
    }

    /// <summary>
    /// The two unrequested outcomes an external-source write can produce that neither the compiler
    /// nor the re-export can see.
    ///
    /// <para>
    /// The object count is the important one. GenerateBlocksFromSource creates whatever the source
    /// declares and has no notion of the object it was addressed to, so a write that landed on the
    /// wrong software scope leaves the addressed block intact — it compiles clean and re-exports
    /// non-empty, and postcondition verification passes. A count other than 1 is the only cheap
    /// signal that this route did something besides update the single block it was pointed at, and
    /// this route has no automated coverage. Same rationale as
    /// <c>PlcTypeImportResult.GeneratedObjectCount</c>.
    /// </para>
    /// </summary>
    private static List<string> BuildSourceWriteWarnings(
        BlockAddress address,
        bool projectNodeRemoved,
        int generatedObjectCount)
    {
        var warnings = new List<string>();

        if (!projectNodeRemoved)
        {
            warnings.Add(
                "A temporary external source node could not be removed and is still in the project. "
                + "Delete it in TIA Portal under the PLC's external source files.");
        }

        if (generatedObjectCount != 1)
        {
            warnings.Add(
                $"TIA Portal generated {generatedObjectCount} objects from the submitted source; "
                + $"expected 1. Inspect '{address.ToDisplayPath()}' and its PLC for objects this "
                + "write was not addressed to.");
        }

        return warnings;
    }

    /// <summary>
    /// Compiles the PLC and re-exports the block as source.
    ///
    /// <para>
    /// The PLC is compiled as a whole rather than just the block — unlike the Simatic ML route —
    /// because rewriting a global DB's declaration silently invalidates every block that reads its
    /// members, and the compiler is the only thing that knows which. This mirrors
    /// <see cref="PlcTypePostconditionVerifier"/>, which makes the same trade for the same reason.
    /// </para>
    /// </summary>
    private static BlockPostconditionEvidence VerifySourcePostconditions(
        Project project,
        BlockAddress address,
        string blockPath,
        IReadOnlyList<string> warnings)
    {
        var compileFailure = CompileAndBuildFailureEvidence(
            project, address.PlcName, blockPath: null, "the data block update", warnings);

        if (compileFailure is not null)
        {
            return compileFailure;
        }

        try
        {
            var reExported = BlockExporter.Export(project, blockPath, SourceFormatNames.Source);
            var reExportSucceeded = !string.IsNullOrWhiteSpace(reExported);

            return new BlockPostconditionEvidence(
                compileSucceeded: true,
                reExportSucceeded: reExportSucceeded,
                diagnosticMessage: reExportSucceeded
                    ? "Verified."
                    : "Re-export produced an empty document after the data block update.",
                warnings: warnings);
        }
        catch (Exception exception)
        {
            return new BlockPostconditionEvidence(
                compileSucceeded: true,
                reExportSucceeded: false,
                diagnosticMessage: "Re-export could not complete after the data block update: " + exception.Message,
                warnings: warnings);
        }
    }
}
