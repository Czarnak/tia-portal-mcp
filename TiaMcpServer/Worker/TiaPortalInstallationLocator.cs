using Microsoft.Win32;
using TiaMcpServer.Diagnostics;

namespace TiaMcpServer.Worker;


public sealed record TiaPortalCandidate(
    string Path,
    string Source,
    bool AssembliesPresent,
    IReadOnlyList<string> MissingAssemblies);

public sealed record TiaPortalInstallationResult(
    bool Found,
    string? SelectedPath,
    string? Source,
    IReadOnlyList<TiaPortalCandidate> Candidates);

/// <summary>
/// Locates the TIA Portal V21 Openness API folder from the host (.NET 8) process.
/// Mirrors the precedence of <c>TiaMcpServer.OpennessWorker.Openness.AssemblyResolver</c>
/// (which lives in the net48 worker and cannot be referenced here):
/// 1. <c>TiaPortalV21Dir</c> environment variable (used as-is, points at the net48 API folder).
/// 2. <c>TiaPortalLocation</c> environment variable (Portal root; <c>PublicAPI\V21\net48</c> appended).
/// 3. Siemens installation registry key <c>INSTALLPATH</c> (64- then 32-bit view).
/// 4. Default install path.
/// A candidate is only accepted when the required Openness assemblies are present.
/// </summary>
public static class TiaPortalInstallationLocator
{
    public static readonly string[] RequiredAssemblies =
    {
        "Siemens.Engineering.Base.dll",
        "Siemens.Engineering.Step7.dll"
    };

    private const string TiaPortalV21DirEnvironmentVariable = "TiaPortalV21Dir";
    private const string TiaPortalLocationEnvironmentVariable = "TiaPortalLocation";
    private const string TiaPortalV21RegistrySubKey =
        @"SOFTWARE\Siemens\Automation\InstalledApps\Totally Integrated Automation Portal V21";
    private const string RegistryInstallValueName = "INSTALLPATH";
    private const string PublicApiSuffix = @"PublicAPI\V21\net48";
    private const string StandardOpennessInstallPath =
        @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48";

    public static TiaPortalInstallationResult Locate(
        IEnvironmentVariableService env,
        IRegistryService registry,
        IFileSystemService fileSystem)
    {
        var candidates = new List<TiaPortalCandidate>();

        var envV21Dir = env.Get(TiaPortalV21DirEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(envV21Dir))
        {
            var path = envV21Dir!.Trim().Trim('"');
            AddCandidate(candidates, fileSystem, path, $"env:{TiaPortalV21DirEnvironmentVariable}");
        }

        var envLocation = env.Get(TiaPortalLocationEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(envLocation))
        {
            var root = envLocation!.Trim().Trim('"');
            var path = Path.Combine(root, PublicApiSuffix);
            AddCandidate(candidates, fileSystem, path, $"env:{TiaPortalLocationEnvironmentVariable}");
        }

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            var installPath = registry.GetStringValue(
                RegistryHive.LocalMachine,
                view,
                TiaPortalV21RegistrySubKey,
                RegistryInstallValueName);

            if (!string.IsNullOrWhiteSpace(installPath))
            {
                var path = Path.Combine(installPath!, PublicApiSuffix);
                AddCandidate(candidates, fileSystem, path, $"registry:{view}");
            }
        }

        AddCandidate(candidates, fileSystem, StandardOpennessInstallPath, "default");

        var selected = candidates.FirstOrDefault(c => c.AssembliesPresent);
        return new TiaPortalInstallationResult(
            selected is not null,
            selected?.Path,
            selected?.Source,
            candidates);
    }

    private static void AddCandidate(
        List<TiaPortalCandidate> candidates,
        IFileSystemService fileSystem,
        string path,
        string source)
    {
        var missing = new List<string>();
        var present = fileSystem.DirectoryExists(path);
        if (present)
        {
            foreach (var assembly in RequiredAssemblies)
            {
                if (!fileSystem.FileExists(Path.Combine(path, assembly)))
                {
                    missing.Add(assembly);
                }
            }
            present = missing.Count == 0;
        }
        else
        {
            missing.AddRange(RequiredAssemblies);
        }

        candidates.Add(new TiaPortalCandidate(path, source, present, missing));
    }
}
