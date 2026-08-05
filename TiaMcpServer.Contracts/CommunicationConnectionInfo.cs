using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Lightweight communication-connection summary owned by one device item.
/// Type-specific attribute values are available only through inspect_network_object.
/// </summary>
public sealed class CommunicationConnectionInfo
{
    public string ConnectionType { get; set; } = string.Empty;

    public string LocalConnectionName { get; set; } = string.Empty;

    public string? LocalConnectionId { get; set; }

    public string? PartnerName { get; set; }

    public bool IsValid { get; set; }

    public bool Selectable { get; set; }

    public NetworkObjectSelectorInfo? Selector { get; set; }

    public List<string> SelectorDiagnostics { get; set; } = new List<string>();
}
