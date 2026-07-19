using Microsoft.Win32;

namespace TiaMcpServer.Diagnostics;

public sealed class RegistryService : IRegistryService
{
    public static readonly RegistryService Instance = new();

    public string? GetStringValue(RegistryHive hive, RegistryView view, string subKey, string valueName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var key = baseKey.OpenSubKey(subKey);
        return key?.GetValue(valueName) as string;
    }

    public int? GetIntValue(RegistryHive hive, RegistryView view, string subKey, string valueName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var key = baseKey.OpenSubKey(subKey);
        if (key is null)
        {
            return null;
        }

        var value = key.GetValue(valueName);
        return value switch
        {
            int i => i,
            long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
            _ => null
        };
    }

    public bool KeyExists(RegistryHive hive, RegistryView view, string subKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var key = baseKey.OpenSubKey(subKey);
        return key is not null;
    }
}
