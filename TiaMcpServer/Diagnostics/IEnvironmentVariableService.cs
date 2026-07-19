namespace TiaMcpServer.Diagnostics;

public interface IEnvironmentVariableService
{
    string? Get(string name);
}
