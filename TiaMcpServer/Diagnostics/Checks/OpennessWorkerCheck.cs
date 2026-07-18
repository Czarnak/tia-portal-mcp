using TiaMcpServer.Worker;

namespace TiaMcpServer.Diagnostics.Checks;

public sealed class OpennessWorkerCheck : IDiagnosticCheck
{
    private readonly IApplicationInfoService _appInfo;
    private readonly IFileSystemService _fileSystem;

    public OpennessWorkerCheck(IApplicationInfoService appInfo, IFileSystemService fileSystem)
    {
        _appInfo = appInfo;
        _fileSystem = fileSystem;
    }

    public string Id => "openness-worker";
    public string Name => "Openness worker";

    public DiagnosticCheckResult Run()
    {
        var result = OpennessWorkerLocator.Locate(_appInfo.BaseDirectory, _fileSystem);
        var evidence = Evidence.Empty();

        for (int i = 0; i < result.Candidates.Count; i++)
        {
            var candidate = result.Candidates[i];
            evidence[$"candidate[{i}].path"] = candidate.Path;
            evidence[$"candidate[{i}].exists"] = candidate.Exists.ToString().ToLowerInvariant();
        }

        if (!result.Found || result.SelectedPath is null)
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Failed,
                "TIA Openness worker executable was not found.",
                "Build the solution and ensure the openness-worker folder is beside the MCP server executable, or reinstall the tia-mcp global tool.",
                evidence);
        }

        var path = result.SelectedPath;
        evidence["selectedPath"] = path;

        var version = _fileSystem.GetFileVersion(path);
        evidence["workerVersion"] = version;

        var directory = Path.GetDirectoryName(path);
        if (directory is null)
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Failed,
                $"Worker executable found at {path} but its directory could not be determined.",
                "Rebuild the solution or reinstall the tia-mcp global tool.",
                evidence);
        }

        var runtimeConfigPath = Path.Combine(directory, "TiaMcpServer.OpennessWorker.runtimeconfig.json");
        var runtimeConfigExists = _fileSystem.FileExists(runtimeConfigPath);
        evidence["runtimeConfigPath"] = runtimeConfigPath;
        evidence["runtimeConfigExists"] = runtimeConfigExists.ToString().ToLowerInvariant();

        if (!runtimeConfigExists)
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Failed,
                $"Worker executable found at {path} but its runtime configuration file is missing.",
                "Rebuild the solution or reinstall the tia-mcp global tool; the openness-worker folder is incomplete.",
                evidence);
        }

        var message = version is not null
            ? $"Worker found at {path} (version {version})."
            : $"Worker found at {path}.";

        return new DiagnosticCheckResult(
            Id,
            Name,
            DiagnosticStatus.Passed,
            message,
            null,
            evidence);
    }
}
