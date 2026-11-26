namespace TiaMcpServer.Plc;

/// <summary>Version of the active PLC read JSON contract.</summary>
public static class PlcContractVersion
{
    public const string Current = "1.0";
}

/// <summary>Version of the plc_write JSON contract; 2.1 adds block text patches.</summary>
public static class PlcWriteContractVersion
{
    public const string Current = "2.1";
}
