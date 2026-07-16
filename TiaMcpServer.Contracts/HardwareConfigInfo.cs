using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public class HardwareConfigInfo
{
    public List<DeviceInfo> Devices { get; set; } = new List<DeviceInfo>();

    public List<SubnetInfo> Subnets { get; set; } = new List<SubnetInfo>();

    /// <summary>Non-fatal degradation notes: members that could not be read and were omitted.</summary>
    public List<string> Messages { get; set; } = new List<string>();
}
