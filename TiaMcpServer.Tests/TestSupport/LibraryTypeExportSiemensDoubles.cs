namespace Siemens.Engineering.SW;

public enum DocumentResultState { Success = 0 }
public sealed class DocumentExportResult
{
    public DocumentResultState State { get; set; }
    public IList<FileInfo> ExportedDocuments { get; set; } = new List<FileInfo>();
}
