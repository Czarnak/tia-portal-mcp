using Microsoft.Win32;
using TiaMcpServer.Diagnostics;

namespace TiaMcpServer.Tests.Diagnostics;

public sealed class FakeApplicationInfoService : IApplicationInfoService
{
    public bool IsWindows { get; set; } = true;
    public string OsName { get; set; } = "Windows";
    public string OsVersion { get; set; } = "10.0.19045";
    public string ProcessArchitecture { get; set; } = "X64";
    public string HostVersion { get; set; } = "1.0.0";
    public string BaseDirectory { get; set; } = "/app";
    public string RuntimeDescription { get; set; } = ".NET 8.0.0";
}

public sealed class FakeEnvironmentVariableService : IEnvironmentVariableService
{
    private readonly Dictionary<string, string?> _vars = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string name, string? value) => _vars[name] = value;

    public string? Get(string name) => _vars.TryGetValue(name, out var v) ? v : null;
}

public sealed class FakeRegistryService : IRegistryService
{
    private readonly Dictionary<(RegistryHive, RegistryView, string, string), string?> _stringValues = new();
    private readonly Dictionary<(RegistryHive, RegistryView, string, string), int?> _intValues = new();
    private readonly HashSet<(RegistryHive, RegistryView, string)> _existingKeys = new();

    public void SetStringValue(RegistryHive hive, RegistryView view, string subKey, string valueName, string? value)
        => _stringValues[(hive, view, subKey, valueName)] = value;

    public void SetIntValue(RegistryHive hive, RegistryView view, string subKey, string valueName, int? value)
        => _intValues[(hive, view, subKey, valueName)] = value;

    public void SetKeyExists(RegistryHive hive, RegistryView view, string subKey)
        => _existingKeys.Add((hive, view, subKey));

    public string? GetStringValue(RegistryHive hive, RegistryView view, string subKey, string valueName)
        => _stringValues.TryGetValue((hive, view, subKey, valueName), out var v) ? v : null;

    public int? GetIntValue(RegistryHive hive, RegistryView view, string subKey, string valueName)
        => _intValues.TryGetValue((hive, view, subKey, valueName), out var v) ? v : null;

    public bool KeyExists(RegistryHive hive, RegistryView view, string subKey)
        => _existingKeys.Contains((hive, view, subKey));
}

public sealed class FakeFileSystemService : IFileSystemService
{
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _fileVersions = new(StringComparer.OrdinalIgnoreCase);

    public void AddFile(string path) => _files.Add(path);
    public void AddDirectory(string path) => _directories.Add(path);
    public void SetFileVersion(string path, string? version) => _fileVersions[path] = version;

    public bool FileExists(string path) => _files.Contains(path);
    public bool DirectoryExists(string path) => _directories.Contains(path);
    public string? GetFileVersion(string path) => _fileVersions.TryGetValue(path, out var v) ? v : null;
}

public sealed class FakeWindowsIdentityService : IWindowsIdentityService
{
    public bool IsWindows { get; set; } = true;
    public WindowsUserInfo? UserInfo { get; set; } = new("DOMAIN\\user", "S-1-5-21-123");
    public OpennessGroupMembership Membership { get; set; } = new(true, true, "S-1-5-32-544", null);

    public WindowsUserInfo? GetCurrentUserInfo() => UserInfo;
    public OpennessGroupMembership CheckGroupMembership(string groupName) => Membership;
}

public sealed class FakeProcessEnumerationService : IProcessEnumerationService
{
    public List<ProcessInfo> Processes { get; set; } = new();

    public IReadOnlyList<ProcessInfo> ListProcesses() => Processes;
}
