using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public class SubnetInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The subnet's own identity, as reported by the engineering system. Empty when it could not be
    /// read; an empty identity must never satisfy a write selector.
    /// </summary>
    public string SubnetId { get; set; } = string.Empty;

    /// <summary>The subnet's particular net type, for example Ethernet or Profibus.</summary>
    public string? NetworkType { get; set; }

    public string? TypeIdentifier { get; set; }

    public List<IoSystemInfo> IoSystems { get; set; } = new List<IoSystemInfo>();

    public List<string> ConnectedNodeNames { get; set; } = new List<string>();
}
