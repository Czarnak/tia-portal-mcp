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
    /// produces Siemens external-source text and is available for a global data block or an
    /// SCL-language FB/FC/OB. The host normalizes this to exactly one of the two before it reaches
    /// the worker.
    /// </param>
    /// <param name="withDependencies">
    /// When true, the exported source carries the block's dependency closure and therefore declares
    /// several objects. Such a document is context only — a write refuses it. Ignored for
    /// <see cref="SourceFormatNames.Xml"/>.
    /// </param>
    public static string Export(
        Project project,
        string blockPath,
        string format,
        bool withDependencies = false)
    {
        var address = BlockAddress.Parse(blockPath);
        var target = BlockTargetResolver.ResolveForExport(project, address);

        if (!string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal))
        {
            return ExportSource(target, address, withDependencies);
        }

        return ExportXml(target, address, requireAuthoritativeXml: false);
    }

    internal static string ExportForSafety(Project project, string blockPath)
    {
        var address = BlockAddress.Parse(blockPath);
        var target = BlockTargetResolver.ResolveForExport(project, address);
        return ExportXml(target, address, requireAuthoritativeXml: true);
    }

    private static string ExportXml(ResolvedBlockTarget target, BlockAddress address, bool requireAuthoritativeXml)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "tia-mcp-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            return ExportXmlBundle(
                address.ToDisplayPath(),
                target.DocumentName,
                () =>
                {
                    // Sole authoritative Simatic ML export path, shared with public reads.
                    string xmlPath = Path.Combine(tempDir, target.DocumentName + ".xml");
                    target.Block!.Export(new FileInfo(xmlPath), ExportOptions.None);
                    return BlockXmlSanitizer.RemoveDocumentInfo(File.ReadAllText(xmlPath));
                },
                documents =>
                {
                    DocumentExportResult result = target.Block!.ExportAsDocuments(
                        new DirectoryInfo(tempDir), target.DocumentName);
                    if (result.State != DocumentResultState.Success)
                    {
                        throw new InvalidOperationException(
                            $"TIA Portal returned document export state '{result.State}'.");
                    }

                    foreach (FileInfo file in result.ExportedDocuments)
                    {
                        documents.Add(new BlockImportDocument(
                            file.Name, file.Name, File.ReadAllText(file.FullName)));
                    }
                },
                requireAuthoritativeXml);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Exports one block as Siemens external-source text, raw and unbundled: unlike the XML route,
    /// which carries an .xml plus a companion .s7dcl/.s7res pair and therefore needs
    /// BlockBundleFormat's delimiters, this is a single document with nothing to delimit.
    ///
    /// <para>
    /// GenerateOptions is always passed explicitly. The two-argument overload's default is
    /// undocumented, and the difference matters: with dependencies the file declares several
    /// objects and a write will refuse it, so the caller has to have asked for that.
    /// </para>
    /// </summary>
    private static string ExportSource(
        ResolvedBlockTarget target,
        BlockAddress address,
        bool withDependencies)
    {
        var decision = DecideSourceFormat(target.Block, address);

        string tempDir = Path.Combine(Path.GetTempPath(), "tia-mcp-block-source-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var path = Path.Combine(tempDir, target.DocumentName + decision.Extension);

            // The resolver's own group, not one re-derived from the block: a unit-scoped block must
            // be generated by the unit's external source group.
            target.ExternalSourceGroup.GenerateSource(
                new List<IGenerateSource> { target.Block! },
                new FileInfo(path),
                withDependencies ? GenerateOptions.WithDependencies : GenerateOptions.None);

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
    /// Decides whether external-source text is defined for this block, throwing the refusal as a
    /// validation error if not.
    ///
    /// <para>
    /// The kind and language are reduced to plain strings here so the decision itself lives in
    /// SourceFormatEligibility, which is Siemens-free and therefore unit-tested. This method is the
    /// only part that needs a live Openness object.
    /// </para>
    /// </summary>
    internal static SourceFormatDecision DecideSourceFormat(PlcBlock? block, BlockAddress address)
    {
        if (block is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"No block was found at '{address.ToDisplayPath()}'.");
        }

        var decision = SourceFormatEligibility.Decide(
            BlockKindName(block),
            block.ProgrammingLanguage.ToString(),
            address.ToDisplayPath());

        if (!decision.IsAllowed)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                decision.RefusalMessage!);
        }

        return decision;
    }

    private static string BlockKindName(PlcBlock block) => block switch
    {
        GlobalDB => "GlobalDB",
        InstanceDB => "InstanceDB",
        ArrayDB => "ArrayDB",
        OB => "OB",
        FB => "FB",
        FC => "FC",
        _ => block.GetType().Name
    };
}
