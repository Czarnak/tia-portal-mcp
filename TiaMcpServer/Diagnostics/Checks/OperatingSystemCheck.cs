namespace TiaMcpServer.Diagnostics.Checks;

public sealed class OperatingSystemCheck : IDiagnosticCheck
{
    private readonly IApplicationInfoService _appInfo;

    public OperatingSystemCheck(IApplicationInfoService appInfo) => _appInfo = appInfo;

    public string Id => "operating-system";
    public string Name => "Operating system";

    public DiagnosticCheckResult Run()
    {
        var evidence = Evidence.Empty();
        evidence["isWindows"] = _appInfo.IsWindows.ToString().ToLowerInvariant();
        evidence["osName"] = _appInfo.OsName;
        evidence["osVersion"] = _appInfo.OsVersion;
        evidence["processArchitecture"] = _appInfo.ProcessArchitecture;

        if (!_appInfo.IsWindows)
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Failed,
                $"{_appInfo.OsName} is not supported. TIA Portal MCP requires Windows.",
                "Run on a Windows host with TIA Portal V21 installed.",
                evidence);
        }

        return new DiagnosticCheckResult(
            Id,
            Name,
            DiagnosticStatus.Passed,
            $"{_appInfo.OsName} {_appInfo.OsVersion} {_appInfo.ProcessArchitecture}",
            null,
            evidence);
    }
}
