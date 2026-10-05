using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>Format-tagged hash of the exact served text of a PLC content read.</summary>
public static class PlcContentHashes
{
    public static string Compute(string format, string content) => ContentHashes.Compute(format, content);
}
