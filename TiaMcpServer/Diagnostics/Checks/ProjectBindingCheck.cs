namespace TiaMcpServer.Diagnostics.Checks;

public sealed class ProjectBindingCheck : IDiagnosticCheck
{
    private const string ProjectArg = "--project";
    private const string ProjectEnvVar = "TIA_MCP_PROJECT_PATH";

    private readonly IEnvironmentVariableService _env;
    private readonly string? _cliProjectPath;

    public ProjectBindingCheck(IEnvironmentVariableService env, string? cliProjectPath)
    {
        _env = env;
        _cliProjectPath = cliProjectPath;
    }

    public string Id => "project-binding";
    public string Name => "Project binding";

    public DiagnosticCheckResult Run()
    {
        var evidence = Evidence.Empty();
        evidence[$"arg:{ProjectArg}"] = _cliProjectPath;
        evidence[$"env:{ProjectEnvVar}"] = _env.Get(ProjectEnvVar);

        var cliPath = string.IsNullOrWhiteSpace(_cliProjectPath) ? null : _cliProjectPath!.Trim();
        var envPath = _env.Get(ProjectEnvVar);
        var envPathNormalized = string.IsNullOrWhiteSpace(envPath) ? null : envPath!.Trim();

        if (cliPath is not null)
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Passed,
                $"Project binding configured via --project: {cliPath}",
                null,
                evidence);
        }

        if (envPathNormalized is not null)
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Passed,
                $"Project binding configured via {ProjectEnvVar}: {envPathNormalized}",
                null,
                evidence);
        }

        return new DiagnosticCheckResult(
            Id,
            Name,
            DiagnosticStatus.Passed,
            "No project binding configured. Tools will use the project currently open in TIA Portal.",
            null,
            evidence);
    }
}
