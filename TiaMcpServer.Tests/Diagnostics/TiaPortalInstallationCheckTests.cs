using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using TiaMcpServer.Worker;
using System.Runtime.Versioning;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

[SupportedOSPlatform("windows")]
public class TiaPortalInstallationCheckTests
{
    [Fact]
    public void FoundViaEnvVar_ReturnsPassed()
    {
        var env = new FakeEnvironmentVariableService();
        env.Set("TiaPortalV21Dir", @"C:\TIA\PublicAPI\V21\net48");
        var registry = new FakeRegistryService();
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddDirectory(@"C:\TIA\PublicAPI\V21\net48");
        foreach (var asm in TiaPortalInstallationLocator.RequiredAssemblies)
            fileSystem.AddFile(System.IO.Path.Combine(@"C:\TIA\PublicAPI\V21\net48", asm));

        var check = new TiaPortalInstallationCheck(env, registry, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("env:TiaPortalV21Dir", result.Message);
    }

    [Fact]
    public void ExistingApiDirectoryWithoutAssemblies_ReturnsPassed()
    {
        var env = new FakeEnvironmentVariableService();
        env.Set("TiaPortalV21Dir", @"C:\TIA\PublicAPI\V21\net48");
        var registry = new FakeRegistryService();
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddDirectory(@"C:\TIA\PublicAPI\V21\net48");

        var check = new TiaPortalInstallationCheck(env, registry, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("env:TiaPortalV21Dir", result.Message);
    }

    [Fact]
    public void TiaPortalLocationRootWithoutApiDirectory_ReturnsPassedWithRootEvidence()
    {
        const string root = @"C:\Program Files\Siemens\Automation\Portal V21";
        var env = new FakeEnvironmentVariableService();
        env.Set("TiaPortalLocation", root);
        var registry = new FakeRegistryService();
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddDirectory(root);

        var result = new TiaPortalInstallationCheck(env, registry, fileSystem).Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("env:TiaPortalLocation", result.Message);
        Assert.Equal(root, result.Evidence!["selectedInstallationPath"]);
        Assert.Equal(
            Path.Combine(root, @"PublicAPI\V21\net48"),
            result.Evidence["selectedPath"]);
    }

    [Fact]
    public void NotFound_ReturnsFailed()
    {
        var env = new FakeEnvironmentVariableService();
        var registry = new FakeRegistryService();
        var fileSystem = new FakeFileSystemService();
        // No directories or files added - nothing will be found

        var check = new TiaPortalInstallationCheck(env, registry, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("not found", result.Message);
        Assert.NotNull(result.Remediation);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void FoundViaRegistry_ReturnsPassed()
    {
        var env = new FakeEnvironmentVariableService();
        var registry = new FakeRegistryService();
        registry.SetStringValue(Microsoft.Win32.RegistryHive.LocalMachine,
            Microsoft.Win32.RegistryView.Registry64,
            @"SOFTWARE\Siemens\Automation\InstalledApps\Totally Integrated Automation Portal V21",
            "INSTALLPATH", @"C:\Program Files\Siemens\Automation\Portal V21");
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddDirectory(@"C:\Program Files\Siemens\Automation\Portal V21");
        var apiPath = @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48";
        fileSystem.AddDirectory(apiPath);
        foreach (var asm in TiaPortalInstallationLocator.RequiredAssemblies)
            fileSystem.AddFile(System.IO.Path.Combine(apiPath, asm));

        var check = new TiaPortalInstallationCheck(env, registry, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("registry:Registry64", result.Message);
    }

    [Fact]
    public void RegistryRootWithoutApiDirectory_ReturnsPassedWithRootEvidence()
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

        var result = new TiaPortalInstallationCheck(env, registry, fileSystem).Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("registry:Registry64", result.Message);
        Assert.Equal(root, result.Evidence!["selectedInstallationPath"]);
    }

    [Fact]
    public void EarlyLocationRootAndLaterCompleteRegistry_SelectsEarlyInstallation()
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

        var result = new TiaPortalInstallationCheck(env, registry, fileSystem).Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Equal("env:TiaPortalLocation", result.Evidence!["selectedSource"]);
        Assert.Equal(locationRoot, result.Evidence["selectedInstallationPath"]);
    }

    [Fact]
    public void EarlyRegistryRootAndLaterCompleteRegistry_SelectsEarlyInstallation()
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

        var result = new TiaPortalInstallationCheck(env, registry, fileSystem).Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Equal("registry:Registry64", result.Evidence!["selectedSource"]);
        Assert.Equal(registry64Root, result.Evidence["selectedInstallationPath"]);
    }

    [Fact]
    public void IncludesCandidateEvidence()
    {
        var env = new FakeEnvironmentVariableService();
        var registry = new FakeRegistryService();
        var fileSystem = new FakeFileSystemService();

        var check = new TiaPortalInstallationCheck(env, registry, fileSystem);
        var result = check.Run();

        Assert.NotNull(result.Evidence);
        // Should have at least the default candidate
        Assert.True(result.Evidence!.Count > 0);
    }
}
