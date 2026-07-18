namespace TiaMcpServer.Diagnostics.Checks;

public sealed class DotNetRuntimeCheck : IDiagnosticCheck
{
    private readonly IApplicationInfoService _appInfo;

    public DotNetRuntimeCheck(IApplicationInfoService appInfo) => _appInfo = appInfo;

    public string Id => "dotnet-runtime";
    public string Name => ".NET runtime";

    public DiagnosticCheckResult Run()
    {
        var evidence = Evidence.Empty();
        evidence["runtimeDescription"] = _appInfo.RuntimeDescription;
        evidence["processArchitecture"] = _appInfo.ProcessArchitecture;
        evidence["applicationVersion"] = _appInfo.HostVersion;

        return new DiagnosticCheckResult(
            Id,
            Name,
            DiagnosticStatus.Passed,
            $"{_appInfo.RuntimeDescription} ({_appInfo.ProcessArchitecture})",
            null,
            evidence);
    }
}
