namespace TiaMcpServer.Diagnostics;

public sealed class DoctorRunner
{
    private readonly IApplicationInfoService _appInfo;
    private readonly IReadOnlyList<IDiagnosticCheck> _checks;

    public DoctorRunner(IApplicationInfoService appInfo, IReadOnlyList<IDiagnosticCheck> checks)
    {
        _appInfo = appInfo;
        _checks = checks;
    }

    public DoctorReport Run()
    {
        var results = new List<DiagnosticCheckResult>(_checks.Count);
        bool hasUnexpectedCheckFailure = false;

        foreach (var check in _checks)
        {
            try
            {
                results.Add(check.Run());
            }
            catch (Exception ex)
            {
                hasUnexpectedCheckFailure = true;
                var evidence = new Dictionary<string, string?>
                {
                    ["exceptionType"] = ex.GetType().FullName,
                    ["exceptionMessage"] = ex.Message
                };

                results.Add(new DiagnosticCheckResult(
                    check.Id,
                    check.Name,
                    DiagnosticStatus.Failed,
                    $"Unexpected error during check: {ex.Message}",
                    null,
                    evidence));
            }
        }

        var passed = results.Count(r => r.Status == DiagnosticStatus.Passed);
        var warnings = results.Count(r => r.Status == DiagnosticStatus.Warning);
        var failed = results.Count(r => r.Status == DiagnosticStatus.Failed);
        var summary = new DoctorSummary(passed, warnings, failed);
        var status = failed > 0 ? DiagnosticStatus.Failed : warnings > 0 ? DiagnosticStatus.Warning : DiagnosticStatus.Passed;

        return new DoctorReport(
            status,
            DateTimeOffset.UtcNow,
            _appInfo.HostVersion,
            summary,
            results)
        {
            HasUnexpectedCheckFailure = hasUnexpectedCheckFailure
        };
    }
}
