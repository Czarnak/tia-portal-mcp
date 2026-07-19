using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class AssemblyResolver
{
    private const string TiaPortalV21DirEnvironmentVariable = "TiaPortalV21Dir";
    private const string TiaPortalLocationEnvironmentVariable = "TiaPortalLocation";
    private const string TiaPortalV21RegistrySubKey =
        @"SOFTWARE\Siemens\Automation\InstalledApps\Totally Integrated Automation Portal V21";
    private const string StandardOpennessInstallPath =
        @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48";
    private const string PublicApiSuffix = @"PublicAPI\V21\net48";

    private static readonly string[] RequiredAssemblies =
    {
        "Siemens.Engineering.Base.dll",
        "Siemens.Engineering.Step7.dll"
    };

    public static void Register()
    {
        AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
    }

    internal static string ExpandTiaPortalLocation(string tiaPortalRoot)
    {
        return Path.Combine(tiaPortalRoot.Trim().Trim('"'), PublicApiSuffix);
    }

    internal static IEnumerable<string> CreateCandidatePaths(
        string? tiaPortalV21Dir,
        string? tiaPortalLocation,
        IEnumerable<string?> registryInstallPaths,
        string defaultPath)
    {
        if (!string.IsNullOrWhiteSpace(tiaPortalV21Dir))
        {
            yield return tiaPortalV21Dir!.Trim().Trim('"');
        }

        if (!string.IsNullOrWhiteSpace(tiaPortalLocation))
        {
            yield return ExpandTiaPortalLocation(tiaPortalLocation!);
        }

        foreach (var registryInstallPath in registryInstallPaths)
        {
            if (!string.IsNullOrWhiteSpace(registryInstallPath))
            {
                yield return ExpandTiaPortalLocation(registryInstallPath!);
            }
        }

        yield return defaultPath;
    }

    internal static string? SelectFirstCompleteCandidate(
        IEnumerable<string> candidates,
        Func<string, bool> isComplete,
        Action<string>? inspected = null)
    {
        foreach (var candidate in candidates)
        {
            inspected?.Invoke(candidate);
            if (isComplete(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        var requestedName = new AssemblyName(args.Name);

        if (requestedName.Name is null ||
            !requestedName.Name.StartsWith("Siemens.Engineering.", StringComparison.Ordinal))
        {
            return null;
        }

        var opennessInstallPath = GetOpennessInstallPath();
        var assemblyPath = Path.Combine(opennessInstallPath, $"{requestedName.Name}.dll");

        if (!File.Exists(assemblyPath))
        {
            return null;
        }

        var loadedAssembly = Assembly.LoadFrom(assemblyPath);

        if (!string.Equals(requestedName.FullName, loadedAssembly.GetName().FullName, StringComparison.Ordinal))
        {
            throw new FileNotFoundException("TIA Portal Openness version does not match the requested assembly.", assemblyPath);
        }

        return loadedAssembly;
    }

    private static string GetOpennessInstallPath()
    {
        var checkedLocations = new List<string>();
        var environmentPath = Environment.GetEnvironmentVariable(TiaPortalV21DirEnvironmentVariable);
        var tiaPortalLocation = Environment.GetEnvironmentVariable(TiaPortalLocationEnvironmentVariable);
        var registryInstallPaths = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? GetRegistryInstallPaths(checkedLocations)
            : Enumerable.Empty<string?>();
        var candidates = CreateCandidatePaths(
            environmentPath,
            tiaPortalLocation,
            registryInstallPaths,
            StandardOpennessInstallPath);
        var selected = SelectFirstCompleteCandidate(
            candidates,
            ContainsRequiredAssemblies,
            path => checkedLocations.Add(path));

        if (selected is not null)
        {
            return selected;
        }

        throw new FileNotFoundException(
            "TIA Portal V21 Openness assemblies were not found. Checked locations: " +
            string.Join("; ", checkedLocations) +
            $". Set {TiaPortalV21DirEnvironmentVariable} to the folder containing Siemens.Engineering.*.dll files.");
    }

    private static IEnumerable<string?> GetRegistryInstallPaths(List<string> checkedLocations)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield break;
        }

        foreach (var registryView in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            var registryPath = $@"HKLM\{TiaPortalV21RegistrySubKey} ({registryView})";
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
            using var key = baseKey.OpenSubKey(TiaPortalV21RegistrySubKey);

            if (key is null)
            {
                checkedLocations.Add($"{registryPath}: key not found");
                continue;
            }

            var installPath = key.GetValue("INSTALLPATH") as string;
            if (installPath is null || installPath.Trim().Length == 0)
            {
                checkedLocations.Add($"{registryPath}: INSTALLPATH not set");
                continue;
            }

            yield return installPath;
        }
    }

    private static bool ContainsRequiredAssemblies(string directoryPath)
    {
        return Directory.Exists(directoryPath) &&
            RequiredAssemblies.All(assemblyName => File.Exists(Path.Combine(directoryPath, assemblyName)));
    }
}
