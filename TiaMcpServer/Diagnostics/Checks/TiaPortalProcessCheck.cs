namespace TiaMcpServer.Diagnostics.Checks;

public sealed class TiaPortalProcessCheck : IDiagnosticCheck
{
    private static readonly string[] TiaPortalProcessNameFragments =
    {
        "Siemens.Automation.Portal",
        "TIA.Portal",
        "Portal.V21",
        "Siemens.Simulation.Portal"
    };

    private readonly IProcessEnumerationService _processes;
    private readonly IApplicationInfoService _appInfo;

    public TiaPortalProcessCheck(IProcessEnumerationService processes, IApplicationInfoService appInfo)
    {
        _processes = processes;
        _appInfo = appInfo;
    }

    public string Id => "tia-portal-process";
    public string Name => "TIA Portal process";

    public DiagnosticCheckResult Run()
    {
        var evidence = Evidence.Empty();

        if (!_appInfo.IsWindows)
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Warning,
                "Not running on Windows; TIA Portal process detection was skipped.",
                "Start TIA Portal V21 before operations that attach to an open project.",
                evidence);
        }

        var processes = _processes.ListProcesses();
        var matches = processes
            .Where(p => TiaPortalProcessNameFragments.Any(f => p.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToList();

        if (matches.Count > 0)
        {
            for (int i = 0; i < matches.Count; i++)
            {
                evidence[$"process[{i}].name"] = matches[i].Name;
                evidence[$"process[{i}].id"] = matches[i].Id.ToString();
            }

            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Passed,
                $"TIA Portal is running ({matches.Count} process(es) detected).",
                null,
                evidence);
        }

        return new DiagnosticCheckResult(
            Id,
            Name,
            DiagnosticStatus.Warning,
            "No running TIA Portal instance was detected.",
            "Start TIA Portal V21 before operations that attach to an open project. This is informational; the installation can still be valid.",
            evidence);
    }
}
