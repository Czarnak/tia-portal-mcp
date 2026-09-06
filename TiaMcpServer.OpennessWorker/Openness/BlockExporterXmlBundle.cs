using System;
using System.Collections.Generic;
using System.Xml.Linq;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static partial class BlockExporter
{
    // Shared orchestration for the existing XML export and the stricter safety read.
    // Callbacks retain the one authoritative Siemens Export path and make failure policy
    // executable in offline tests without loading Siemens assemblies.
    internal static string ExportXmlBundle(
        string displayPath,
        string documentName,
        Func<string> exportXml,
        Action<ICollection<BlockImportDocument>> exportCompanions,
        bool requireAuthoritativeXml)
    {
        var documents = new List<BlockImportDocument>();
        var xmlExported = false;
        try
        {
            var content = exportXml();
            if (requireAuthoritativeXml)
            {
                // Validate without reserializing: the snapshot must retain exact bytes.
                _ = XDocument.Parse(content);
            }

            var xmlName = documentName + ".xml";
            documents.Add(new BlockImportDocument(xmlName, xmlName, content));
            xmlExported = true;
        }
        catch (Exception)
        {
            if (requireAuthoritativeXml)
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.WorkerOperationFailed,
                    "Authoritative Simatic ML XML could not be exported for a project-tree safety snapshot.");
            }

            // Preserve the public best-effort companion-only fallback.
        }

        try
        {
            // Companion exports are one package. Do not expose an incomplete package when
            // ExportAsDocuments (or a later file read) fails after producing earlier files.
            var companionDocuments = new List<BlockImportDocument>();
            exportCompanions(companionDocuments);
            documents.AddRange(companionDocuments);
        }
        catch (Exception exception)
        {
            var outcome = BlockDocumentPackagePolicy.Decide(xmlExported, displayPath, exception.Message);
            if (outcome.IsFatal)
            {
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, outcome.Message);
            }

            Console.Error.WriteLine(outcome.Message);
        }

        return BlockBundleFormat.Compose(documents);
    }
}
