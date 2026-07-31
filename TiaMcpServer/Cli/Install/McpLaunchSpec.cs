namespace TiaMcpServer.Cli.Install;

internal sealed record McpLaunchSpec(
    string ServerName,
    string ExecutablePath,
    IReadOnlyList<string> Arguments);
