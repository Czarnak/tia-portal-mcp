using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public class NodeInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The node's own identity within its device, as reported by the engineering system. Empty when
    /// it could not be read; an empty identity must never satisfy a write selector.
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>The node's particular type, for example Ethernet or Profibus.</summary>
    public string? NodeType { get; set; }

    public string? IpAddress { get; set; }

    public string? SubnetMask { get; set; }

    public string? PnDeviceName { get; set; }

    public string? SubnetName { get; set; }

    public string? IoSystemName { get; set; }

    /// <summary>True when a deterministic selector for this node was successfully constructed.</summary>
    public bool Selectable { get; set; }

    /// <summary>
    /// Selector that can be forwarded directly into an inspect_network_object request.
    /// Null when <see cref="Selectable"/> is false.
    /// </summary>
    public NetworkObjectSelectorInfo? Selector { get; set; }

    /// <summary>Diagnostic messages explaining why the selector is absent or degraded.</summary>
    public List<string> SelectorDiagnostics { get; set; } = new();
}
