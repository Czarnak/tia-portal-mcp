using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Evidence about a network object, captured at read time so callers can correlate the result
/// with the selector that produced it without re-reading the hardware configuration.
/// </summary>
public class NetworkObjectEvidenceInfo
{
    /// <summary>Kind of the resolved network object (one of <see cref="NetworkObjectKinds"/>).</summary>
    public string? Kind { get; set; }

    /// <summary>Selector that was used to locate this object.</summary>
    public NetworkObjectSelectorInfo? Selector { get; set; }

    /// <summary>
    /// Non-fatal notes captured while reading this object (e.g. properties that could not be resolved).
    /// </summary>
    public List<string> Messages { get; set; } = new List<string>();
}
