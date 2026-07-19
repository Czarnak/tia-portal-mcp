namespace TiaMcpServer.Diagnostics;

public sealed record DoctorReport(
    DiagnosticStatus Status,
    DateTimeOffset TimestampUtc,
    string HostVersion,
    DoctorSummary Summary,
    IReadOnlyList<DiagnosticCheckResult> Checks)
{
    public bool HasUnexpectedCheckFailure { get; init; }
}
