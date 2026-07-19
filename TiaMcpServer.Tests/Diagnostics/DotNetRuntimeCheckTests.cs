using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class DotNetRuntimeCheckTests
{
    [Fact]
    public void AlwaysPasses_WithRuntimeDescription()
    {
        var appInfo = new FakeApplicationInfoService
        {
            RuntimeDescription = ".NET 8.0.5",
            ProcessArchitecture = "X64",
            HostVersion = "1.0.0"
        };
        var check = new DotNetRuntimeCheck(appInfo);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains(".NET 8.0.5", result.Message);
        Assert.Contains("X64", result.Message);
    }

    [Fact]
    public void IncludesEvidence()
    {
        var appInfo = new FakeApplicationInfoService
        {
            RuntimeDescription = ".NET 8.0.5",
            ProcessArchitecture = "Arm64",
            HostVersion = "2.0.0"
        };
        var check = new DotNetRuntimeCheck(appInfo);

        var result = check.Run();

        Assert.NotNull(result.Evidence);
        Assert.Equal(".NET 8.0.5", result.Evidence!["runtimeDescription"]);
        Assert.Equal("Arm64", result.Evidence!["processArchitecture"]);
        Assert.Equal("2.0.0", result.Evidence!["applicationVersion"]);
    }
}
