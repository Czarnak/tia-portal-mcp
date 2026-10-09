namespace TiaMcpServer.Contracts.CrossReferences;

/// <summary>
/// Cross-references of one target. A leaf target is one owner query; a container fans out over
/// the owners beneath it. <see cref="IsComplete"/> is false when any owner or projection failed or
/// <c>maxResults</c> cut the sources; <see cref="Messages"/> then explains why.
/// </summary>
public class CrossReferenceReport
{
    public CrossReferenceSelectorInfo Target { get; set; } = new CrossReferenceSelectorInfo();

    public string Filter { get; set; } = CrossReferenceFilterNames.Default;

    public bool IsComplete { get; set; }

    public int OwnerQueryCount { get; set; }

    public int SuccessfulOwnerQueryCount { get; set; }

    public List<string> Messages { get; set; } = new List<string>();

    public List<CrossReferenceSourceInfo> Sources { get; set; } = new List<CrossReferenceSourceInfo>();

    public int TotalSourceCount { get; set; }

    public int TotalReferenceCount { get; set; }

    public int TotalLocationCount { get; set; }

    /// <summary>Top-level sources dropped by the host response budget; the worker always writes 0.</summary>
    public int OmittedSourceCount { get; set; }
}
