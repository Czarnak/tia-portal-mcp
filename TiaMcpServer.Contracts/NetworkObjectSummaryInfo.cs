using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Summary of one network object as returned by a <c>list_network_objects</c> page.
/// Contains the selector needed to inspect the object in a follow-up call.
/// </summary>
public sealed class NetworkObjectSummaryInfo
{
    /// <summary>Kind of the network object (one of <see cref="NetworkObjectKinds"/>).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Whether the summary contains a complete selector that can be inspected.</summary>
    public bool Selectable { get; set; }

    /// <summary>Selector that uniquely identifies this object for an <c>inspect_network_object</c> call.</summary>
    public NetworkObjectSelectorInfo? Selector { get; set; }

    /// <summary>Identity and relationship evidence captured for this object.</summary>
    public NetworkObjectEvidenceInfo Evidence { get; set; } = new NetworkObjectEvidenceInfo();

    /// <summary>Deterministic diagnostics explaining why a selector could not be emitted.</summary>
    public List<string> Diagnostics { get; set; } = new List<string>();
}
