using TiaMcpServer.Contracts;

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
    private readonly McpAccessMode _accessMode;
    private readonly bool _hasConfiguredProjectBinding;

    public TiaPortalProcessCheck(
        IProcessEnumerationService processes,
        IApplicationInfoService appInfo,
        McpAccessMode accessMode,
        bool hasConfiguredProjectBinding)
    {
        _processes = processes;
        _appInfo = appInfo;
        _accessMode = accessMode;
        _hasConfiguredProjectBinding = hasConfiguredProjectBinding;
    }

    public string Id => "tia-portal-process";
    public string Name => "TIA Portal process";

    public DiagnosticCheckResult Run()
    {
        var evidence = Evidence.Empty();
        evidence["accessMode"] = _accessMode == McpAccessMode.ReadOnly ? "read-only" : "read-write";
        evidence["projectBindingConfigured"] = _hasConfiguredProjectBinding.ToString();

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

            if (matches.Count == 1)
            {
                return new DiagnosticCheckResult(
                    Id,
                    Name,
                    DiagnosticStatus.Passed,
                    "TIA Portal is running (1 process detected). Doctor did not attach or inspect its open project.",
                    null,
                    evidence);
            }

            var unsafeUnboundReadWrite =
                _accessMode == McpAccessMode.ReadWrite && !_hasConfiguredProjectBinding;
            var status = unsafeUnboundReadWrite
                ? DiagnosticStatus.Failed
                : DiagnosticStatus.Warning;
            var message = unsafeUnboundReadWrite
                ? $"Multiple TIA Portal processes were detected: {matches.Count} processes while read-write mode has no explicit project binding."
                : $"Multiple TIA Portal processes were detected: {matches.Count} processes. Doctor cannot determine which process has the intended project open without attaching.";

            return new DiagnosticCheckResult(
                Id,
                Name,
                status,
                message,
                _hasConfiguredProjectBinding
                    ? "Before using project tools, confirm that the configured .ap21 project is open in exactly one TIA Portal process."
                    : "Configure an explicit --project binding, or close the unintended TIA Portal instances before using project tools.",
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
