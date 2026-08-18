using TiaMcpServer.Contracts;

namespace TiaMcpServer.Diagnostics.Checks;

public sealed class ProjectBindingCheck : IDiagnosticCheck
{
    private const string ProjectArg = "--project";
    private const string ProjectEnvVar = "TIA_MCP_PROJECT_PATH";

    private readonly IEnvironmentVariableService _env;
    private readonly IFileSystemService _fileSystem;
    private readonly string? _cliProjectPath;
    private readonly McpAccessMode _accessMode;

    public ProjectBindingCheck(
        IEnvironmentVariableService env,
        IFileSystemService fileSystem,
        string? cliProjectPath,
        McpAccessMode accessMode)
    {
        _env = env;
        _fileSystem = fileSystem;
        _cliProjectPath = cliProjectPath;
        _accessMode = accessMode;
    }

    public string Id => "project-binding";
    public string Name => "Project binding";

    public DiagnosticCheckResult Run()
    {
        var evidence = Evidence.Empty();
        evidence[$"arg:{ProjectArg}"] = _cliProjectPath;
        var envPath = _env.Get(ProjectEnvVar);
        evidence[$"env:{ProjectEnvVar}"] = envPath;
        evidence["accessMode"] = ModeLabel(_accessMode);

        var cliPath = string.IsNullOrWhiteSpace(_cliProjectPath) ? null : _cliProjectPath!.Trim();
        var envPathNormalized = string.IsNullOrWhiteSpace(envPath) ? null : envPath!.Trim();
        var configuredPath = cliPath ?? envPathNormalized;
        var bindingSource = cliPath is not null ? ProjectArg : envPathNormalized is not null ? ProjectEnvVar : null;
        evidence["bindingSource"] = bindingSource;

        if (configuredPath is null)
        {
            var status = _accessMode == McpAccessMode.ReadWrite
                ? DiagnosticStatus.Failed
                : DiagnosticStatus.Warning;
            var message = _accessMode == McpAccessMode.ReadWrite
                ? "Read-write mode has no explicit project binding. This configuration is not safe to report as ready."
                : "No project binding is configured. Read-only calls would rely on an unverified project currently open in TIA Portal.";

            return new DiagnosticCheckResult(
                Id,
                Name,
                status,
                message,
                $"Set {ProjectArg} to the absolute path of the intended .ap21 project, or set {ProjectEnvVar}.",
                evidence);
        }

        if (!Path.IsPathFullyQualified(configuredPath))
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Failed,
                $"Project binding configured via {bindingSource} is not an absolute path: {configuredPath}",
                "Use the absolute path of the intended TIA Portal V21 .ap21 project.",
                evidence);
        }

        if (!string.Equals(Path.GetExtension(configuredPath), ".ap21", StringComparison.OrdinalIgnoreCase))
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Failed,
                $"Project binding configured via {bindingSource} is not a TIA Portal V21 .ap21 file: {configuredPath}",
                "Point the binding at the exact .ap21 project file, not its containing directory or an archive.",
                evidence);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(configuredPath);
        }
        catch (Exception ex)
        {
            evidence["pathValidationException"] = ex.GetType().FullName;
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Failed,
                $"Project binding configured via {bindingSource} is not a valid local path: {configuredPath}",
                "Correct the project path and rerun doctor.",
                evidence);
        }

        evidence["normalizedProjectPath"] = fullPath;
        var fileExists = _fileSystem.FileExists(fullPath);
        evidence["projectFileExists"] = fileExists.ToString();
        evidence["liveProjectMatchChecked"] = bool.FalseString;

        if (!fileExists)
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Failed,
                $"Project binding configured via {bindingSource} does not exist: {fullPath}",
                "Correct the path or restore the .ap21 project file before starting the MCP server.",
                evidence);
        }

        return new DiagnosticCheckResult(
            Id,
            Name,
            DiagnosticStatus.Warning,
            $"Project binding configured via {bindingSource} points to an existing .ap21 file: {fullPath}. "
                + "Doctor did not attach to TIA Portal or verify which process has it open.",
            "Before using project tools, open this exact project in one TIA Portal process and run get_project_status to establish a verified runtime binding.",
            evidence);
    }

    internal static bool HasConfiguredBinding(IEnvironmentVariableService env, string? cliProjectPath)
        => !string.IsNullOrWhiteSpace(cliProjectPath) ||
           !string.IsNullOrWhiteSpace(env.Get(ProjectEnvVar));

    private static string ModeLabel(McpAccessMode mode)
        => mode == McpAccessMode.ReadOnly ? "read-only" : "read-write";
}
