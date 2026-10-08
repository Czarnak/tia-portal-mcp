namespace TiaMcpServer.Contracts;

public class CatalogEntryInfo
{
    public string TypeName { get; set; } = string.Empty;

    public string? ArticleNumber { get; set; }

    public string? Version { get; set; }

    public string TypeIdentifier { get; set; } = string.Empty;

    public string? TypeIdentifierNormalized { get; set; }

    public string? CatalogPath { get; set; }

    public string? Description { get; set; }
}
