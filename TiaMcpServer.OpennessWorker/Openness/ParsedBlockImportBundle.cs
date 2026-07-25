using System;
using System.Collections.Generic;

namespace TiaMcpServer.OpennessWorker.Openness;

internal sealed class ParsedBlockImportBundle
{
    public ParsedBlockImportBundle(
        string primaryDocumentName,
        IReadOnlyList<BlockImportDocument> documents)
    {
        PrimaryDocumentName = primaryDocumentName ?? throw new ArgumentNullException(nameof(primaryDocumentName));
        Documents = new List<BlockImportDocument>(
            documents ?? throw new ArgumentNullException(nameof(documents))).AsReadOnly();
    }

    public string PrimaryDocumentName { get; }

    public IReadOnlyList<BlockImportDocument> Documents { get; }
}
