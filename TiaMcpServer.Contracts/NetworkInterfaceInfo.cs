using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public class NetworkInterfaceInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>True when a deterministic selector for this network interface was successfully constructed.</summary>
    public bool Selectable { get; set; }

    /// <summary>
    /// Selector that can be forwarded directly into an inspect_network_object request.
    /// Null when <see cref="Selectable"/> is false.
    /// </summary>
    public NetworkObjectSelectorInfo? Selector { get; set; }

    /// <summary>Diagnostic messages explaining why the selector is absent or degraded.</summary>
    public List<string> SelectorDiagnostics { get; set; } = new();

    public List<NodeInfo> Nodes { get; set; } = new List<NodeInfo>();
}
