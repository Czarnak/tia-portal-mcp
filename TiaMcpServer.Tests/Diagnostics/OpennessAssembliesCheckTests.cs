using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using TiaMcpServer.Worker;
using System.Runtime.Versioning;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

[SupportedOSPlatform("windows")]
public class OpennessAssembliesCheckTests
{
    [Fact]
    public void AllAssembliesPresent_ReturnsPassed()
    {
        var env = new FakeEnvironmentVariableService();
        env.Set("TiaPortalV21Dir", @"C:\TIA\api");
        var registry = new FakeRegistryService();
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddDirectory(@"C:\TIA\api");
        foreach (var asm in TiaPortalInstallationLocator.RequiredAssemblies)
            fileSystem.AddFile(System.IO.Path.Combine(@"C:\TIA\api", asm));

        var check = new OpennessAssembliesCheck(env, registry, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("present", result.Message);
    }

    [Fact]
    public void ExistingApiDirectoryWithoutAllAssemblies_ReportsMissingAssemblies()
    {
        var env = new FakeEnvironmentVariableService();
        env.Set("TiaPortalV21Dir", @"C:\TIA\api");
        var registry = new FakeRegistryService();
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddDirectory(@"C:\TIA\api");
        // Only add one of the required assemblies.
        fileSystem.AddFile(System.IO.Path.Combine(@"C:\TIA\api", TiaPortalInstallationLocator.RequiredAssemblies[0]));

        var check = new OpennessAssembliesCheck(env, registry, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("Missing Openness assemblies", result.Message);
        Assert.Contains(TiaPortalInstallationLocator.RequiredAssemblies[1], result.Message);
        Assert.Equal(@"C:\TIA\api", result.Evidence!["selectedPath"]);
        Assert.NotNull(result.Remediation);
    }

    [Fact]
    public void TiaPortalLocationRootWithoutApiDirectory_ReportsMissingApiDirectory()
    {
        const string root = @"C:\Program Files\Siemens\Automation\Portal V21";
        var env = new FakeEnvironmentVariableService();
        env.Set("TiaPortalLocation", root);
        var registry = new FakeRegistryService();
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddDirectory(root);

        var result = new OpennessAssembliesCheck(env, registry, fileSystem).Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("Openness API directory", result.Message);
        Assert.Equal(root, result.Evidence!["selectedInstallationPath"]);
        Assert.Equal("false", result.Evidence["apiDirectoryPresent"]);
        Assert.Contains("Openness option", result.Remediation);
    }

    [Fact]
    public void RegistryRootWithoutApiDirectory_ReportsMissingApiDirectory()
    {
        const string root = @"C:\Program Files\Siemens\Automation\Portal V21";
        var env = new FakeEnvironmentVariableService();
        var registry = new FakeRegistryService();
        registry.SetStringValue(
            Microsoft.Win32.RegistryHive.LocalMachine,
            Microsoft.Win32.RegistryView.Registry64,
            @"SOFTWARE\Siemens\Automation\InstalledApps\Totally Integrated Automation Portal V21",
            "INSTALLPATH",
            root);
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddDirectory(root);

        var result = new OpennessAssembliesCheck(env, registry, fileSystem).Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("Openness API directory", result.Message);
        Assert.Equal(root, result.Evidence!["selectedInstallationPath"]);
        Assert.Equal("registry:Registry64", result.Evidence["selectedSource"]);
    }

    [Fact]
    public void EarlyLocationRootAndLaterCompleteRegistry_UsesCompleteRegistryApi()
    {
        const string locationRoot = @"C:\TIA\Portal V21";
        const string registryRoot = @"D:\Siemens\Portal V21";
        var env = new FakeEnvironmentVariableService();
        env.Set("TiaPortalLocation", locationRoot);
        var registry = new FakeRegistryService();
        registry.SetStringValue(
            Microsoft.Win32.RegistryHive.LocalMachine,
            Microsoft.Win32.RegistryView.Registry64,
            @"SOFTWARE\Siemens\Automation\InstalledApps\Totally Integrated Automation Portal V21",
            "INSTALLPATH",
            registryRoot);
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddDirectory(locationRoot);
        fileSystem.AddDirectory(registryRoot);
        var registryApi = Path.Combine(registryRoot, @"PublicAPI\V21\net48");
        fileSystem.AddDirectory(registryApi);
        foreach (var assembly in TiaPortalInstallationLocator.RequiredAssemblies)
            fileSystem.AddFile(Path.Combine(registryApi, assembly));

        var result = new OpennessAssembliesCheck(env, registry, fileSystem).Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Equal("registry:Registry64", result.Evidence!["selectedSource"]);
        Assert.Equal(registryApi, result.Evidence["selectedPath"]);
    }

    [Fact]
    public void EarlyRegistryRootAndLaterCompleteRegistry_UsesCompleteRegistryApi()
    {
        const string registry64Root = @"C:\Siemens\Portal V21";
        const string registry32Root = @"D:\Siemens\Portal V21";
        var env = new FakeEnvironmentVariableService();
        var registry = new FakeRegistryService();
        const string registryKey = @"SOFTWARE\Siemens\Automation\InstalledApps\Totally Integrated Automation Portal V21";
        registry.SetStringValue(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64, registryKey, "INSTALLPATH", registry64Root);
        registry.SetStringValue(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32, registryKey, "INSTALLPATH", registry32Root);
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddDirectory(registry64Root);
        fileSystem.AddDirectory(registry32Root);
        var registry32Api = Path.Combine(registry32Root, @"PublicAPI\V21\net48");
        fileSystem.AddDirectory(registry32Api);
        foreach (var assembly in TiaPortalInstallationLocator.RequiredAssemblies)
            fileSystem.AddFile(Path.Combine(registry32Api, assembly));

        var result = new OpennessAssembliesCheck(env, registry, fileSystem).Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Equal("registry:Registry32", result.Evidence!["selectedSource"]);
        Assert.Equal(registry32Api, result.Evidence["selectedPath"]);
    }

    [Fact]
    public void NoInstallationFound_ReturnsFailed()
    {
        var env = new FakeEnvironmentVariableService();
        var registry = new FakeRegistryService();
        var fileSystem = new FakeFileSystemService();

        var check = new OpennessAssembliesCheck(env, registry, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("No TIA Portal", result.Message);
    }
}
