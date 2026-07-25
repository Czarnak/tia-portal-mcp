using System.Text;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static partial class BlockExporter
{
    internal static BlockPostconditionEvidence VerifyPrimaryDocument(
        Project project,
        string blockPath,
        string primaryDocumentName)
    {
        try
        {
            var address = BlockAddress.Parse(blockPath);
            var target = BlockTargetResolver.ResolveForExport(project, address);
            return VerifyPrimaryDocument(
                target.DocumentName,
                primaryDocumentName,
                (directory, documentName) =>
                {
                    var result = target.Block!.ExportAsDocuments(directory, documentName);
                    var documentPath = Path.Combine(directory.FullName, documentName);
                    return result.State == DocumentResultState.Success
                        && File.Exists(documentPath)
                        && new FileInfo(documentPath).Length > 0;
                });
        }
        catch (Exception exception)
        {
            return new BlockPostconditionEvidence(
                compileSucceeded: true,
                reExportSucceeded: false,
                diagnosticMessage: "Re-export could not complete after block import: " + exception.Message);
        }
    }

    public static string Export(Project project, string blockPath)
    {
        var address = BlockAddress.Parse(blockPath);
        var target = BlockTargetResolver.ResolveForExport(project, address);

        string tempDir = Path.Combine(Path.GetTempPath(), "tia-mcp-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var documents = new List<BlockImportDocument>();

            // Simatic ML XML (FlgNet) — the authoritative document for update_block_logic.
            // Export() requires a consistent block. When it fails we emit no XML document at
            // all rather than a placeholder: a placeholder would round-trip back into
            // update_block_logic as a real document name and be staged to disk.
            try
            {
                string xmlPath = Path.Combine(tempDir, target.DocumentName + ".xml");
                target.Block!.Export(new FileInfo(xmlPath), ExportOptions.None);
                var xmlName = target.DocumentName + ".xml";
                documents.Add(new BlockImportDocument(
                    xmlName,
                    xmlName,
                    BlockXmlSanitizer.RemoveDocumentInfo(File.ReadAllText(xmlPath))));
            }
            catch (Exception)
            {
                // Intentionally no document. The write path reports this as an actionable
                // error when it finds no Simatic ML document in the bundle.
            }

            // s7dcl documents package (human-readable rung text) — read-only context.
            DocumentExportResult result = target.Block!.ExportAsDocuments(
                new DirectoryInfo(tempDir), target.DocumentName);

            if (result.State != DocumentResultState.Success)
                throw new InvalidOperationException($"Export failed with state: {result.State}");

            foreach (FileInfo file in result.ExportedDocuments)
            {
                documents.Add(new BlockImportDocument(
                    file.Name, file.Name, File.ReadAllText(file.FullName)));
            }

            return BlockBundleFormat.Compose(documents);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
