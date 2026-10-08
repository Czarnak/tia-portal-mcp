namespace TiaMcpServer.Contracts;

public class CrossReferenceLocationInfo
{
    public string Name { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    /// <summary>One of <see cref="CrossReferenceAccessNames.All"/>.</summary>
    public string Access { get; set; } = string.Empty;

    /// <summary>One of <see cref="CrossReferenceTypeNames.All"/>.</summary>
    public string ReferenceType { get; set; } = string.Empty;

    public string ReferenceLocation { get; set; } = string.Empty;

    /// <summary>The engineering object the reference resolves through; null when Openness reports none.</summary>
    public CrossReferenceObjectRefInfo? ReferencedAs { get; set; }

    public string ReferencedAsName { get; set; } = string.Empty;
}

public class CrossReferenceObjectRefInfo
{
    public string Name { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;
}
