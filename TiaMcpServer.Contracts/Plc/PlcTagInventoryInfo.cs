namespace TiaMcpServer.Contracts;

/// <summary>
/// Tag inventory of one or more PLCs. <see cref="IsComplete"/> is false when any table, group,
/// tag or constant could not be read; each such failure has one entry in <see cref="Messages"/>.
/// </summary>
public class PlcTagInventoryInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public List<PlcTagInventoryPlcInfo> Plcs { get; set; } = new List<PlcTagInventoryPlcInfo>();
}

public class PlcTagInventoryPlcInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string? DeviceName { get; set; }

    public List<TagTableInfo> Tables { get; set; } = new List<TagTableInfo>();
}
