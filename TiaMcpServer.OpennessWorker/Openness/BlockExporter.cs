using System.Text;
using System.Xml.Linq;
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
            var combined = new StringBuilder();

            // Simatic ML XML (FlgNet) — the exact format consumed by update_block_logic import.
            // ExportOptions.None yields the minimal form closest to a valid import document.
            // Export() requires a consistent block; a freshly created / inconsistent block cannot
            // be exported this way, so treat the XML section as best-effort and skip it on failure.
            // ExportAsDocuments below still provides the s7dcl content for those blocks.
            try
            {
                string xmlPath = Path.Combine(tempDir, target.DocumentName + ".xml");
                target.Block!.Export(new FileInfo(xmlPath), ExportOptions.None);
                combined.Append($"--- FILE: {target.DocumentName}.xml ---\n");
                combined.Append(StripNonDeterministic(File.ReadAllText(xmlPath)));
            }
            catch (Exception ex)
            {
                combined.Append($"--- FILE: {target.DocumentName}.xml (unavailable) ---\n");
                combined.Append($"<!-- FlgNet XML export unavailable: {ex.Message} -->\n");
            }

            // s7dcl documents package (human-readable rung text).
            DocumentExportResult result = target.Block!.ExportAsDocuments(new DirectoryInfo(tempDir), target.DocumentName);

            if (result.State != DocumentResultState.Success)
                throw new InvalidOperationException($"Export failed with state: {result.State}");

            foreach (FileInfo file in result.ExportedDocuments)
            {
                combined.Append($"--- FILE: {file.Name} ---\n");
                combined.Append(File.ReadAllText(file.FullName));
            }

            return combined.ToString();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Removes non-deterministic content from exported Simatic ML XML so the result is stable
    /// across calls. The &lt;DocumentInfo&gt; element carries a &lt;Created&gt; timestamp that changes
    /// every export; including it would make get_block_content non-idempotent and invalidate the
    /// write-safety state hash (which binds to GetBlockContent output) on every preview/apply.
    /// </summary>
    private static string StripNonDeterministic(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            doc.Root?.Elements()
                .Where(e => e.Name.LocalName == "DocumentInfo")
                .Remove();
            return doc.ToString();
        }
        catch
        {
            // If parsing fails for any reason, fall back to the raw text.
            return xml;
        }
    }
}
