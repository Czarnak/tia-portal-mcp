namespace Siemens.Engineering.SW;

public enum DocumentResultState { Success = 0 }
public sealed class DocumentExportResult
{
    public DocumentResultState State { get; set; }
    public IList<FileInfo> ExportedDocuments { get; set; } = new List<FileInfo>();
}

public enum ImportDocumentOptions { Override = 0 }
public sealed class DocumentImportResultForBlocks
{
    public DocumentResultState State { get; set; }
}
