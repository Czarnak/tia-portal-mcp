using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class ProjectBindingCheckTests
{
    private const string ProjectPath = @"C:\Projects\Line.ap21";

    [Fact]
    public void CliExistingAbsoluteAp21Path_ReturnsWarningUntilLiveBindingIsVerified()
    {
        var env = new FakeEnvironmentVariableService();
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddFile(ProjectPath);
        var check = new ProjectBindingCheck(env, fileSystem, ProjectPath, McpAccessMode.ReadOnly);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("--project", result.Message);
        Assert.Contains(ProjectPath, result.Message);
        Assert.Contains("did not attach", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(bool.FalseString, result.Evidence!["liveProjectMatchChecked"]);
    }

    [Fact]
    public void ExistingEnvironmentPath_ReturnsWarningUntilLiveBindingIsVerified()
    {
        var env = new FakeEnvironmentVariableService();
        env.Set("TIA_MCP_PROJECT_PATH", ProjectPath);
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddFile(ProjectPath);
        var check = new ProjectBindingCheck(env, fileSystem, null, McpAccessMode.ReadOnly);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("TIA_MCP_PROJECT_PATH", result.Message);
    }

    [Fact]
    public void NoBinding_ReadOnly_ReturnsWarning()
    {
        var env = new FakeEnvironmentVariableService();
        var check = new ProjectBindingCheck(
            env,
            new FakeFileSystemService(),
            null,
            McpAccessMode.ReadOnly);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("No project binding", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Remediation);
    }

    [Fact]
    public void NoBinding_ReadWrite_ReturnsFailed()
    {
        var env = new FakeEnvironmentVariableService();
        var check = new ProjectBindingCheck(
            env,
            new FakeFileSystemService(),
            null,
            McpAccessMode.ReadWrite);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("Read-write", result.Message);
    }

    [Fact]
    public void CliPathTakesPrecedence()
    {
        var env = new FakeEnvironmentVariableService();
        env.Set("TIA_MCP_PROJECT_PATH", @"C:\Other.ap21");
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddFile(ProjectPath);
        var check = new ProjectBindingCheck(env, fileSystem, ProjectPath, McpAccessMode.ReadOnly);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("--project", result.Message);
        Assert.DoesNotContain("TIA_MCP_PROJECT_PATH", result.Message);
    }

    [Fact]
    public void WhitespaceCliPath_TreatedAsUnboundReadOnlyWarning()
    {
        var env = new FakeEnvironmentVariableService();
        var check = new ProjectBindingCheck(
            env,
            new FakeFileSystemService(),
            "   ",
            McpAccessMode.ReadOnly);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("No project binding", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingProjectFile_ReturnsFailed()
    {
        var env = new FakeEnvironmentVariableService();
        var check = new ProjectBindingCheck(
            env,
            new FakeFileSystemService(),
            ProjectPath,
            McpAccessMode.ReadOnly);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("does not exist", result.Message);
    }

    [Fact]
    public void RelativeProjectPath_ReturnsFailedBeforeFileSystemProbe()
    {
        var env = new FakeEnvironmentVariableService();
        var check = new ProjectBindingCheck(
            env,
            new FakeFileSystemService(),
            @"Projects\Line.ap21",
            McpAccessMode.ReadOnly);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("not an absolute path", result.Message);
    }

    [Fact]
    public void NonAp21Binding_ReturnsFailed()
    {
        var env = new FakeEnvironmentVariableService();
        var check = new ProjectBindingCheck(
            env,
            new FakeFileSystemService(),
            @"C:\Projects\Line.zap21",
            McpAccessMode.ReadOnly);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("not a TIA Portal V21 .ap21 file", result.Message);
    }

    [Fact]
    public void HasConfiguredBinding_UsesCliOrEnvironmentWithoutClaimingValidity()
    {
        var env = new FakeEnvironmentVariableService();

        Assert.False(ProjectBindingCheck.HasConfiguredBinding(env, null));
        Assert.True(ProjectBindingCheck.HasConfiguredBinding(env, ProjectPath));

        env.Set("TIA_MCP_PROJECT_PATH", ProjectPath);
        Assert.True(ProjectBindingCheck.HasConfiguredBinding(env, null));
    }
}
