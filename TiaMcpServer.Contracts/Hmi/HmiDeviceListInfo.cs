namespace TiaMcpServer.Contracts.Hmi;

/// <summary>
/// Result of <c>list_hmi_devices</c>. <see cref="IsComplete"/> is false when a device item could not be
/// read; each such failure has one entry in <see cref="Messages"/>.
/// </summary>
public class HmiDeviceListInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public List<HmiDeviceInfo> Devices { get; set; } = new List<HmiDeviceInfo>();
}

/// <summary>One HMI software instance. <see cref="Kind"/> is <c>unified</c> or <c>classic</c>.</summary>
public class HmiDeviceInfo
{
    public string DeviceName { get; set; } = string.Empty;

    public string SoftwareName { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string? TypeIdentifier { get; set; }
}
