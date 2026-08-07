namespace TiaMcpServer.Cli.Install;

internal sealed record ExecutableResolutionResult(
    bool Found,
    string Command,
    string? ResolvedPath,
    ExecutableKind Kind,
    string? Error);
