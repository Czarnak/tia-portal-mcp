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
}
