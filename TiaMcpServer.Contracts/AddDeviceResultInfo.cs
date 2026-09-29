namespace TiaMcpServer.Contracts;

public class AddDeviceResultInfo
{
    public string DeviceName { get; set; } = string.Empty;

    public string RootItemName { get; set; } = string.Empty;

    public string TypeIdentifier { get; set; } = string.Empty;

    public List<string> Warnings { get; set; } = new List<string>();
}
