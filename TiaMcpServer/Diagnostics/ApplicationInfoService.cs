using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TiaMcpServer.Diagnostics;

public sealed class ApplicationInfoService : IApplicationInfoService
{
    public static readonly ApplicationInfoService Instance = new();

    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private readonly IRegistryService _registry;
    private readonly Func<bool> _isWindows;
    private readonly Func<Version> _getOsVersion;
    private readonly Func<Assembly?> _getEntryAssembly;

    public ApplicationInfoService()
        : this(
            RegistryService.Instance,
            OperatingSystem.IsWindows,
            () => Environment.OSVersion.Version,
            Assembly.GetEntryAssembly)
    {
    }

    public ApplicationInfoService(
        IRegistryService registry,
        Func<bool> isWindows,
        Func<Version> getOsVersion)
        : this(registry, isWindows, getOsVersion, Assembly.GetEntryAssembly)
    {
    }

    public ApplicationInfoService(
        IRegistryService registry,
        Func<bool> isWindows,
        Func<Version> getOsVersion,
        Func<Assembly?> getEntryAssembly)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _isWindows = isWindows ?? throw new ArgumentNullException(nameof(isWindows));
        _getOsVersion = getOsVersion ?? throw new ArgumentNullException(nameof(getOsVersion));
        _getEntryAssembly = getEntryAssembly ?? throw new ArgumentNullException(nameof(getEntryAssembly));
    }

    public bool IsWindows => _isWindows();

    public string OsName
    {
        get
        {
            if (!IsWindows || !OperatingSystem.IsWindows())
            {
                return RuntimeInformation.OSDescription;
            }

            return GetWindowsProductName();
        }
    }

    public string OsVersion => _getOsVersion().ToString();

    public string ProcessArchitecture => RuntimeInformation.ProcessArchitecture.ToString();

    public string HostVersion
    {
        get
        {
            var assembly = _getEntryAssembly();
            var informationalVersion = assembly?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                return informationalVersion;
            }

            return assembly?.GetName().Version?.ToString() ?? "1.0.0";
        }
    }

    public string BaseDirectory => AppContext.BaseDirectory;

    public string RuntimeDescription => RuntimeInformation.FrameworkDescription;

    [SupportedOSPlatform("windows")]
    private string GetWindowsProductName()
    {
        var version = _getOsVersion();

        try
        {
            var productName = _registry.GetStringValue(
                RegistryHive.LocalMachine,
                RegistryView.Registry64,
                CurrentVersionKey,
                "ProductName");
            if (!string.IsNullOrWhiteSpace(productName))
            {
                return NormalizeWindowsProductName(productName, version);
            }
        }
        catch
        {
            // Registry access may fail in restricted environments.
        }

        // Fallback: use build number heuristic (Windows 11+ has build >= 22000).
        return IsWindows11OrLater(version) ? "Windows 11" : "Windows 10";
    }

    private static string NormalizeWindowsProductName(string productName, Version version)
        => IsWindows11OrLater(version) && !productName.Contains("Server", StringComparison.OrdinalIgnoreCase)
            ? productName.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase)
            : productName;

    private static bool IsWindows11OrLater(Version version)
        => version.Major > 10 || version.Major == 10 && version.Build >= 22000;
}
