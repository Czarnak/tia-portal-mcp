using TiaMcpServer.Worker;

namespace TiaMcpServer.Diagnostics.Checks;

public sealed class OpennessAssembliesCheck : IDiagnosticCheck
{
    private readonly IEnvironmentVariableService _env;
    private readonly IRegistryService _registry;
    private readonly IFileSystemService _fileSystem;

    public OpennessAssembliesCheck(
        IEnvironmentVariableService env,
        IRegistryService registry,
        IFileSystemService fileSystem)
    {
        _env = env;
        _registry = registry;
        _fileSystem = fileSystem;
    }

    public string Id => "openness-assemblies";
    public string Name => "Openness assemblies";

    public DiagnosticCheckResult Run()
    {
        var result = TiaPortalInstallationLocator.Locate(_env, _registry, _fileSystem);
        var evidence = Evidence.Empty();

        if (!result.Found || result.SelectedPath is null)
        {
            evidence["selectedPath"] = null;
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Failed,
                "No TIA Portal V21 installation was found.",
                "Install TIA Portal V21 with the Openness option, or set TiaPortalV21Dir to the PublicAPI\\V21\\net48 folder.",
                evidence);
        }

        var path = result.SelectedOpennessPath ?? result.SelectedPath;
        var source = result.OpennessSource ?? result.Source;
        var selectedCandidate = result.Candidates.First(candidate =>
            candidate.Path == path && candidate.Source == source);
        evidence["selectedPath"] = path;
        evidence["selectedInstallationPath"] = selectedCandidate.InstallationPath;
        evidence["selectedSource"] = selectedCandidate.Source;
        evidence["apiDirectoryPresent"] = selectedCandidate.DirectoryPresent.ToString().ToLowerInvariant();

        if (!selectedCandidate.DirectoryPresent)
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Failed,
                $"TIA Portal V21 is installed at {selectedCandidate.InstallationPath}, but its Openness API directory was not found at {path}.",
                "Modify or reinstall TIA Portal V21 with the Openness option, or set TiaPortalV21Dir to a complete PublicAPI\\V21\\net48 folder.",
                evidence);
        }

        var missing = new List<string>();

        foreach (var assembly in TiaPortalInstallationLocator.RequiredAssemblies)
        {
            var assemblyPath = Path.Combine(path, assembly);
            var exists = _fileSystem.FileExists(assemblyPath);
            evidence[$"assembly.{assembly}.path"] = assemblyPath;
            evidence[$"assembly.{assembly}.exists"] = exists.ToString().ToLowerInvariant();
            if (!exists)
            {
                missing.Add(assembly);
            }
        }

        if (missing.Count > 0)
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Failed,
                $"Missing Openness assemblies: {string.Join(", ", missing)}.",
                "Reinstall TIA Portal V21 with the Openness option, or set TiaPortalV21Dir to a complete PublicAPI\\V21\\net48 folder.",
                evidence);
        }

        return new DiagnosticCheckResult(
            Id,
            Name,
            DiagnosticStatus.Passed,
            $"Required Openness assemblies present in {path}.",
            null,
            evidence);
    }
}
