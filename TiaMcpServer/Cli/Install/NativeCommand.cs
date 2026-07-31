namespace TiaMcpServer.Cli.Install;

internal sealed record NativeCommand(
    string Executable,
    IReadOnlyList<string> Arguments,
    bool Interactive);
