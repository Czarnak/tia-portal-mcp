namespace TiaMcpServer.Contracts;

public class PlcOnlineResultInfo
{
    public bool Success { get; set; } = true;
    public string Operation { get; set; } = string.Empty;
    public string? ProjectPath { get; set; }
    public string PlcName { get; set; } = string.Empty;
}
