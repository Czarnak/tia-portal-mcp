using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Detailed result returned by an <c>inspect_network_object</c> operation.
/// </summary>
public sealed class NetworkObjectInspectionInfo
{
    /// <summary>Verified selector for the object that was inspected.</summary>
    public NetworkObjectSelectorInfo Target { get; set; } = new NetworkObjectSelectorInfo();

    /// <summary>Typed evidence captured while inspecting the object.</summary>
    public NetworkObjectEvidenceInfo Evidence { get; set; } = new NetworkObjectEvidenceInfo();

    /// <summary>Attribute values requested by the caller (may be empty when none were requested).</summary>
    public List<NetworkAttributeInfo> Attributes { get; set; } = new List<NetworkAttributeInfo>();

    /// <summary>Non-fatal notes captured during inspection.</summary>
    public List<string> Messages { get; set; } = new List<string>();
}
