using System.Text;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
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
                    if (result.State != DocumentResultState.Success)
                    {
                        return false;
                    }

                    foreach (FileInfo exported in result.ExportedDocuments)
                    {
                        if (exported.Exists && exported.Length > 0)
                        {
                            return true;
                        }
                    }

                    return false;
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

    /// <param name="format">
    /// <see cref="SourceFormatNames.Xml"/> (the block default) produces the multi-document bundle
    /// this operation has always returned, byte for byte. <see cref="SourceFormatNames.Source"/>
    /// produces Siemens external-source text and is only available for a global data block.
    /// The host normalizes this to exactly one of the two before it reaches the worker.
    /// </param>
    public static string Export(Project project, string blockPath, string format)
    {
        var address = BlockAddress.Parse(blockPath);
        var target = BlockTargetResolver.ResolveForExport(project, address);

        if (!string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal))
        {
            return ExportSource(target, address);
        }

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

    /// <summary>
    /// Exports one global data block as Siemens external-source text (.db), raw and unbundled:
    /// unlike the XML route, which carries an .xml plus a companion .s7dcl/.s7res pair and
    /// therefore needs BlockBundleFormat's delimiters, this is a single document with nothing
    /// to delimit.
    /// </summary>
    private static string ExportSource(ResolvedBlockTarget target, BlockAddress address)
    {
        var dataBlock = RequireGlobalDb(target.Block, address);

        string tempDir = Path.Combine(Path.GetTempPath(), "tia-mcp-block-source-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var path = Path.Combine(tempDir, target.DocumentName + ".db");

            // The resolver's own group, not one re-derived from the block: a unit-scoped block must
            // be generated by the unit's external source group.
            target.ExternalSourceGroup.GenerateSource(
                new List<IGenerateSource> { dataBlock },
                new FileInfo(path));

            if (!File.Exists(path))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.WorkerOperationFailed,
                    $"TIA Portal reported no error but produced no source file for "
                    + $"'{target.DocumentName}'. Compile the block in TIA Portal and try again.");
            }

            return SourceTextEncoding.ForTransport(File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// External-source text is only defined for a global data block. Every other block kind is
    /// refused by name so the caller knows what it actually addressed and which format to use.
    /// </summary>
    internal static GlobalDB RequireGlobalDb(PlcBlock? block, BlockAddress address)
    {
        if (block is GlobalDB globalDb)
        {
            return globalDb;
        }

        if (block is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"No block was found at '{address.ToDisplayPath()}'.");
        }

        var kind = DescribeBlockKind(block);

        throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError,
            $"'{address.ToDisplayPath()}' is a {kind}, not a global data block. "
            + $"format={SourceFormatNames.Source} is only available for global data blocks; use "
            + $"format={SourceFormatNames.Xml} for a {kind}.");
    }

    private static string DescribeBlockKind(PlcBlock block) => block switch
    {
        InstanceDB => "instance data block (InstanceDB)",
        ArrayDB => "array data block (ArrayDB)",
        OB => "organization block (OB)",
        FB => "function block (FB)",
        FC => "function (FC)",
        _ => block.GetType().Name
    };
}
