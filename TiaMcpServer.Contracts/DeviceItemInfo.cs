using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public class DeviceItemInfo
{
    public string? Name { get; set; }

    public string? TypeIdentifier { get; set; }

    public int? PositionNumber { get; set; }

    public string? Address { get; set; }

    /// <summary>
    /// Always present. An item without network interfaces reports an empty list, so a consumer
    /// walking the hardware tree never has to distinguish "none" from "not modelled".
    /// </summary>
    public List<NetworkInterfaceInfo> NetworkInterfaces { get; set; } = new List<NetworkInterfaceInfo>();

    /// <summary>Always present. A leaf item reports an empty list rather than null.</summary>
    public List<DeviceItemInfo> Items { get; set; } = new List<DeviceItemInfo>();
}
