using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Detailed result returned by an <c>inspect_network_object</c> operation.
/// </summary>
public class NetworkObjectInspectionInfo
{
    /// <summary>Kind of the inspected object (one of <see cref="NetworkObjectKinds"/>).</summary>
    public string? Kind { get; set; }

    /// <summary>Human-readable display name for the object.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Evidence used to locate the object, including the resolved selector.</summary>
    public NetworkObjectEvidenceInfo? Evidence { get; set; }

    /// <summary>Attribute values requested by the caller (may be empty when none were requested).</summary>
    public List<NetworkAttributeInfo> Attributes { get; set; } = new List<NetworkAttributeInfo>();

    /// <summary>Non-fatal notes captured during inspection.</summary>
    public List<string> Messages { get; set; } = new List<string>();
}
