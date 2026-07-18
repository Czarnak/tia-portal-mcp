using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class ProjectBindingCheckTests
{
    [Fact]
    public void CliPathProvided_ReturnsPassed()
    {
        var env = new FakeEnvironmentVariableService();
        var check = new ProjectBindingCheck(env, @"C:\Projects\Line.ap21");

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("--project", result.Message);
        Assert.Contains(@"C:\Projects\Line.ap21", result.Message);
    }

    [Fact]
    public void EnvVarProvided_ReturnsPassed()
    {
        var env = new FakeEnvironmentVariableService();
        env.Set("TIA_MCP_PROJECT_PATH", @"C:\Projects\Line.ap21");
        var check = new ProjectBindingCheck(env, null);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("TIA_MCP_PROJECT_PATH", result.Message);
    }

    [Fact]
    public void NeitherProvided_ReturnsPassed()
    {
        var env = new FakeEnvironmentVariableService();
        var check = new ProjectBindingCheck(env, null);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("No project binding", result.Message);
    }

    [Fact]
    public void CliPathTakesPrecedence()
    {
        var env = new FakeEnvironmentVariableService();
        env.Set("TIA_MCP_PROJECT_PATH", @"C:\Other.ap21");
        var check = new ProjectBindingCheck(env, @"C:\Projects\Line.ap21");

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("--project", result.Message);
        Assert.DoesNotContain("TIA_MCP_PROJECT_PATH", result.Message);
    }

    [Fact]
    public void WhitespaceCliPath_TreatedAsNotProvided()
    {
        var env = new FakeEnvironmentVariableService();
        var check = new ProjectBindingCheck(env, "   ");

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("No project binding", result.Message);
    }
}
