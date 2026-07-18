namespace TiaMcpServer.Diagnostics;

public sealed class EnvironmentVariableService : IEnvironmentVariableService
{
    public static readonly EnvironmentVariableService Instance = new();

    public string? Get(string name) => Environment.GetEnvironmentVariable(name);
}
