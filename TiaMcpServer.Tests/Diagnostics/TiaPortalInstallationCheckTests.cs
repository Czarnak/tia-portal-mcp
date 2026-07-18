using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

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
    public void FoundViaRegistry_ReturnsPassed()
    {
        var env = new FakeEnvironmentVariableService();
        var registry = new FakeRegistryService();
        registry.SetStringValue(Microsoft.Win32.RegistryHive.LocalMachine,
            Microsoft.Win32.RegistryView.Registry64,
            @"SOFTWARE\Siemens\Automation\InstalledApps\Totally Integrated Automation Portal V21",
            "INSTALLPATH", @"C:\Program Files\Siemens\Automation\Portal V21");
        var fileSystem = new FakeFileSystemService();
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
