namespace TiaMcpServer.Contracts;

public class PlcCrossReferenceInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string? DeviceName { get; set; }

    public int OwnerQueryCount { get; set; }

    public int SuccessfulOwnerQueryCount { get; set; }

    // Old payloads cannot establish complete owner coverage.
    public bool IsComplete { get; set; }

    public List<CrossReferenceSourceInfo> Sources { get; set; } = new List<CrossReferenceSourceInfo>();

    public List<string> Messages { get; set; } = new List<string>();

    public int SourceCount { get; set; }

    public int ReferenceCount { get; set; }

    public int LocationCount { get; set; }
}
