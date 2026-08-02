namespace TiaMcpServer.Cli.Install;

public sealed record InstallOptions(
    bool Valid,
    ClientKind? Client,
    string ServerName,
    string AccessMode,
    string? TiaProject,
    string? ServerPath,
    bool DryRun,
    bool Json,
    bool Help,
    string? ParseError);
