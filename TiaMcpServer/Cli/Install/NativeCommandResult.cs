namespace TiaMcpServer.Cli.Install;

internal sealed record NativeCommandResult(
    int ExitCode,
    string Stdout,
    string Stderr);
