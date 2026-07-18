using System.Reflection;
using System.Runtime.InteropServices;

namespace TiaMcpServer.Diagnostics;

public sealed class ApplicationInfoService : IApplicationInfoService
{
    public static readonly ApplicationInfoService Instance = new();

    public bool IsWindows => OperatingSystem.IsWindows();

    public string OsName
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return RuntimeInformation.OSDescription;
            }

            return GetWindowsProductName();
        }
    }

    public string OsVersion => Environment.OSVersion.Version.ToString();

    public string ProcessArchitecture => RuntimeInformation.ProcessArchitecture.ToString();

    public string HostVersion
    {
        get
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            return version is not null ? version.ToString() : "1.0.0";
        }
    }

    public string BaseDirectory => AppContext.BaseDirectory;

    public string RuntimeDescription => RuntimeInformation.FrameworkDescription;

    private static string GetWindowsProductName()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var productName = key?.GetValue("ProductName") as string;
            if (!string.IsNullOrWhiteSpace(productName))
            {
                return productName;
            }
        }
        catch
        {
            // Registry access may fail in restricted environments.
        }

        // Fallback: use build number heuristic (Windows 11+ has build >= 22000).
        var version = Environment.OSVersion.Version;
        return version.Major >= 10 && version.Build >= 22000 ? "Windows 11" : "Windows 10";
    }
}
