namespace TiaMcpServer.Contracts.Block;

public sealed class BlockImportDocument
{
    public BlockImportDocument(string logicalName, string safeFileName, string content)
    {
        LogicalName = logicalName;
        SafeFileName = safeFileName;
        Content = content;
    }

    public string LogicalName { get; }

    public string SafeFileName { get; }

    public string Content { get; }
}
