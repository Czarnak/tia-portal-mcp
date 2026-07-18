namespace TiaMcpServer.Diagnostics;

public sealed record ProcessInfo(string Name, int Id);

public interface IProcessEnumerationService
{
    IReadOnlyList<ProcessInfo> ListProcesses();
}
