using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

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
    public void IncompleteInstallation_ReturnsFailed()
    {
        var env = new FakeEnvironmentVariableService();
        env.Set("TiaPortalV21Dir", @"C:\TIA\api");
        var registry = new FakeRegistryService();
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddDirectory(@"C:\TIA\api");
        // Only add one of the required assemblies - locator considers this incomplete
        fileSystem.AddFile(System.IO.Path.Combine(@"C:\TIA\api", TiaPortalInstallationLocator.RequiredAssemblies[0]));

        var check = new OpennessAssembliesCheck(env, registry, fileSystem);
        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.NotNull(result.Remediation);
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
