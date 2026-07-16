using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public class DeviceItemInfo
{
    public string? Name { get; set; }

    public string? TypeIdentifier { get; set; }

    public int? PositionNumber { get; set; }

    public string? Address { get; set; }

    public List<NetworkInterfaceInfo>? NetworkInterfaces { get; set; }

    public List<DeviceItemInfo>? Items { get; set; }
}
