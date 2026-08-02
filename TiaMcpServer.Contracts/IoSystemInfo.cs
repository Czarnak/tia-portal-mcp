using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public class IoSystemInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The IO system's number within its subnet. Null when it could not be read; a null number must
    /// never satisfy a write selector.
    /// </summary>
    public int? Number { get; set; }

    public string? IoControllerName { get; set; }

    public List<string> ConnectedDeviceNames { get; set; } = new List<string>();
}
