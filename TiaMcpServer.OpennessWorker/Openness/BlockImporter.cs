using System;
using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class BlockImporter
{
    internal static BlockImportResult Import(Project project, string blockPath, string yamlContent)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        if (yamlContent is null) throw new ArgumentNullException(nameof(yamlContent));

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
                    BlockExporter.Export(project, blockPath));
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
        try
        {
            var compileReport = CompileChecker.Compile(project, plcName: null, blockPath);
            if (compileReport.TotalErrorCount != 0
                || string.Equals(compileReport.OverallState, "Error", StringComparison.OrdinalIgnoreCase))
            {
                return new BlockPostconditionEvidence(
                    compileSucceeded: false,
                    reExportSucceeded: false,
                    diagnosticMessage: "Compilation reported errors after block import.");
            }
        }
        catch (Exception exception)
        {
            return new BlockPostconditionEvidence(
                compileSucceeded: false,
                reExportSucceeded: false,
                diagnosticMessage: "Compilation could not complete after block import: " + exception.Message);
        }

        return BlockExporter.VerifyPrimaryDocument(project, blockPath, primaryDocumentName);
    }
}
