namespace TiaMcpServer.Plc;

/// <summary>Version of the active PLC read JSON contract.</summary>
public static class PlcContractVersion
{
    public const string Current = "1.0";
}

/// <summary>Version of the plc_write JSON contract; 2.0 replaced the read-back checks with <c>trusted</c>.</summary>
public static class PlcWriteContractVersion
{
    public const string Current = "2.0";
}
