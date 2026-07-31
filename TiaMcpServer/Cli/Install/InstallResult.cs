namespace TiaMcpServer.Cli.Install;

internal sealed record InstallResult(
    bool Success,
    int ExitCode,
    string? Stdout,
    string? Stderr,
    int? VerificationExitCode,
    string? VerificationStdout,
    string? VerificationStderr);
