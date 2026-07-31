namespace TiaMcpServer.Cli.Install;

internal sealed record ClientDetectionResult(bool Found, string? ExecutablePath, string? Error);
