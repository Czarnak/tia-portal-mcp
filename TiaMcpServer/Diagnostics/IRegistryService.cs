using Microsoft.Win32;

namespace TiaMcpServer.Diagnostics;

public interface IRegistryService
{
    string? GetStringValue(RegistryHive hive, RegistryView view, string subKey, string valueName);

    int? GetIntValue(RegistryHive hive, RegistryView view, string subKey, string valueName);

    bool KeyExists(RegistryHive hive, RegistryView view, string subKey);
}
