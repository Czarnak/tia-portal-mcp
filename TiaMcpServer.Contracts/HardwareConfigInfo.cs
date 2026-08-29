using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts;

public class HardwareConfigInfo
{
    public List<DeviceInfo> Devices { get; set; } = new List<DeviceInfo>();

    public List<SubnetInfo> Subnets { get; set; } = new List<SubnetInfo>();

    /// <summary>Non-fatal degradation notes: members that could not be read and were omitted.</summary>
    public List<string> Messages { get; set; } = new List<string>();

    /// <summary>Present only for an explicit paged hardware-config read.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HardwarePaginationInfo? Pagination { get; set; }
}
