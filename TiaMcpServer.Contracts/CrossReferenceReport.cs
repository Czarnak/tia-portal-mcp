using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public class CrossReferenceReport
{
    public string Filter { get; set; } = CrossReferenceFilterNames.Default;

    public bool IsComplete { get; set; }

    public List<PlcCrossReferenceInfo> Plcs { get; set; } = new List<PlcCrossReferenceInfo>();

    public int TotalSourceCount { get; set; }

    public int TotalReferenceCount { get; set; }

    public int TotalLocationCount { get; set; }
}
