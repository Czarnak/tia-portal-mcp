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
            (directory, primaryDocumentName) => ImportDocuments(
                project,
                preflight.Address,
                directory,
                primaryDocumentName),
            () => VerifyPostconditions(
                project,
                blockPath,
                preflight.Bundle.PrimaryDocumentName));
    }

    private static void ImportDocuments(
        Project project,
        BlockAddress address,
        DirectoryInfo directory,
        string primaryDocumentName)
    {
        var target = BlockTargetResolver.ResolveForImport(project, address);
        var result = target.Group.Blocks.ImportFromDocuments(
            directory,
            primaryDocumentName,
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
