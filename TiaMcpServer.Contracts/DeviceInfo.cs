using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public class DeviceInfo
{
    public string? Name { get; set; }

    public string? TypeIdentifier { get; set; }

    public List<DeviceItemInfo> Items { get; set; } = new List<DeviceItemInfo>();
}
