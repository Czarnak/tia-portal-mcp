using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

public static partial class BlockExporter
{
    // XML export orchestration. Callbacks keep the one Siemens Export path and make the
    // failure policy executable in offline tests without loading Siemens assemblies.
    internal static string ExportXmlBundle(
        string displayPath,
        string documentName,
        Func<string> exportXml,
        Action<ICollection<BlockImportDocument>> exportCompanions)
    {
        var documents = new List<BlockImportDocument>();
        var xmlExported = false;
        try
        {
            var content = exportXml();
            var xmlName = documentName + ".xml";
            documents.Add(new BlockImportDocument(xmlName, xmlName, content));
            xmlExported = true;
        }
        catch (Exception)
        {
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
