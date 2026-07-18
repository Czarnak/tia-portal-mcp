using Microsoft.Win32;

namespace TiaMcpServer.Diagnostics.Checks;

public sealed class DotNetFrameworkCheck : IDiagnosticCheck
{
    private const string NetFrameworkFullKey = @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full";
    private const string ReleaseValueName = "Release";
    private const int NetFramework48Release = 528040;

    private readonly IRegistryService _registry;
    private readonly IApplicationInfoService _appInfo;

    public DotNetFrameworkCheck(IRegistryService registry, IApplicationInfoService appInfo)
    {
        _registry = registry;
        _appInfo = appInfo;
    }

    public string Id => "dotnet-framework";
    public string Name => ".NET Framework 4.8";

    public DiagnosticCheckResult Run()
    {
        var evidence = Evidence.Empty();
        evidence["registryKey"] = $@"HKLM\{NetFrameworkFullKey}";
        evidence["releaseValueName"] = ReleaseValueName;

        if (!_appInfo.IsWindows)
        {
            evidence["reason"] = "not running on Windows";
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Failed,
                "Not running on Windows; .NET Framework 4.8 cannot be detected.",
                "Run on Windows and install .NET Framework 4.8 Developer Pack or Runtime.",
                evidence);
        }

        var release = _registry.GetIntValue(
            RegistryHive.LocalMachine,
            RegistryView.Registry64,
            NetFrameworkFullKey,
            ReleaseValueName);

        if (release is null)
        {
            release = _registry.GetIntValue(
                RegistryHive.LocalMachine,
                RegistryView.Registry32,
                NetFrameworkFullKey,
                ReleaseValueName);
            if (release is not null)
            {
                evidence["registryView"] = "Registry32";
            }
        }
        else
        {
            evidence["registryView"] = "Registry64";
        }

        if (release is null)
        {
            evidence["release"] = null;
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Failed,
                ".NET Framework 4.8 was not detected.",
                "Install .NET Framework 4.8 Runtime (or Developer Pack for source builds): https://dotnet.microsoft.com/download/dotnet-framework/net48",
                evidence);
        }

        evidence["release"] = release.Value.ToString();
        var version = InterpretRelease(release.Value);
        evidence["frameworkVersion"] = version;

        if (release.Value < NetFramework48Release)
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Failed,
                $".NET Framework {version} detected (release {release.Value}); 4.8 or newer is required.",
                "Install .NET Framework 4.8 Runtime (or Developer Pack for source builds): https://dotnet.microsoft.com/download/dotnet-framework/net48",
                evidence);
        }

        return new DiagnosticCheckResult(
            Id,
            Name,
            DiagnosticStatus.Passed,
            $".NET Framework {version} detected (release {release.Value}).",
            null,
            evidence);
    }

    private static string InterpretRelease(int release)
    {
        return release switch
        {
            >= 533320 => "4.8.1",
            >= 528040 => "4.8",
            >= 461808 => "4.7.2",
            >= 461308 => "4.7.1",
            >= 460798 => "4.7",
            >= 394802 => "4.6.2",
            >= 394254 => "4.6.1",
            >= 393295 => "4.6",
            >= 379893 => "4.5.2",
            >= 378675 => "4.5.1",
            >= 378389 => "4.5",
            _ => $"4.x (release {release})"
        };
    }
}
