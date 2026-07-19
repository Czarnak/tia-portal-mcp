using TiaMcpServer.Worker;

namespace TiaMcpServer.Diagnostics.Checks;

public sealed class TiaPortalInstallationCheck : IDiagnosticCheck
{
    private readonly IEnvironmentVariableService _env;
    private readonly IRegistryService _registry;
    private readonly IFileSystemService _fileSystem;

    public TiaPortalInstallationCheck(
        IEnvironmentVariableService env,
        IRegistryService registry,
        IFileSystemService fileSystem)
    {
        _env = env;
        _registry = registry;
        _fileSystem = fileSystem;
    }

    public string Id => "tia-portal-installation";
    public string Name => "TIA Portal installation";

    public DiagnosticCheckResult Run()
    {
        var result = TiaPortalInstallationLocator.Locate(_env, _registry, _fileSystem);
        var evidence = Evidence.Empty();

        for (int i = 0; i < result.Candidates.Count; i++)
        {
            var candidate = result.Candidates[i];
            evidence[$"candidate[{i}].path"] = candidate.Path;
            evidence[$"candidate[{i}].apiPath"] = candidate.Path;
            evidence[$"candidate[{i}].installationPath"] = candidate.InstallationPath;
            evidence[$"candidate[{i}].source"] = candidate.Source;
            evidence[$"candidate[{i}].installationPresent"] = candidate.InstallationPresent.ToString().ToLowerInvariant();
            evidence[$"candidate[{i}].directoryPresent"] = candidate.DirectoryPresent.ToString().ToLowerInvariant();
            evidence[$"candidate[{i}].apiDirectoryPresent"] = candidate.DirectoryPresent.ToString().ToLowerInvariant();
            evidence[$"candidate[{i}].assembliesPresent"] = candidate.AssembliesPresent.ToString().ToLowerInvariant();
        }

        if (result.Found)
        {
            evidence["selectedPath"] = result.SelectedPath;
            evidence["selectedInstallationPath"] = result.SelectedInstallationPath;
            evidence["selectedSource"] = result.Source;
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Passed,
                $"TIA Portal V21 detected at {result.SelectedInstallationPath} via {result.Source}.",
                null,
                evidence);
        }

        return new DiagnosticCheckResult(
            Id,
            Name,
            DiagnosticStatus.Failed,
            "TIA Portal V21 installation was not found in any inspected location.",
            "Install TIA Portal V21, set TiaPortalV21Dir to its PublicAPI\\V21\\net48 folder, or set TiaPortalLocation to the Portal V21 installation root.",
            evidence);
    }
}
