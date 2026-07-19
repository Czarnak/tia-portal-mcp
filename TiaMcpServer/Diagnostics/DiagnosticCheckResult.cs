namespace TiaMcpServer.Diagnostics;

public sealed record DiagnosticCheckResult(
    string Id,
    string Name,
    DiagnosticStatus Status,
    string Message,
    string? Remediation = null,
    IReadOnlyDictionary<string, string?>? Evidence = null);
