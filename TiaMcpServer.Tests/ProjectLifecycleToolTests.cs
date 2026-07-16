using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

public class ProjectLifecycleToolTests
{
    [Theory]
    [InlineData("GetProjectStatus", "get_project_status", false)]
    [InlineData("OpenProject", "open_project", true)]
    [InlineData("CreateProject", "create_project", true)]
    [InlineData("SaveProject", "save_project", true)]
    [InlineData("SaveProjectAs", "save_project_as", true)]
    [InlineData("ArchiveProject", "archive_project", true)]
    [InlineData("CloseProject", "close_project", true)]
    public void ProjectLifecycleToolsHaveMcpMetadata(string methodName, string expectedToolName, bool requiresConfirm)
    {
        var type = typeof(ProjectLifecycleTools);

        Assert.NotNull(type.GetCustomAttribute<McpServerToolTypeAttribute>());

        var method = type.GetMethod(methodName);

        Assert.NotNull(method);
        var toolAttribute = method.GetCustomAttribute<McpServerToolAttribute>();
        Assert.NotNull(toolAttribute);
        Assert.Equal(expectedToolName, toolAttribute.Name);

        var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
        if (requiresConfirm)
        {
            Assert.Contains("confirm=true", description);
        }
    }

    [Theory]
    [InlineData("GetProjectStatusAsync")]
    [InlineData("OpenProjectAsync")]
    [InlineData("CreateProjectAsync")]
    [InlineData("SaveProjectAsync")]
    [InlineData("SaveProjectAsAsync")]
    [InlineData("ArchiveProjectAsync")]
    [InlineData("CloseProjectAsync")]
    public void OpennessWorkerClientExposesProjectLifecycleMethods(string methodName)
    {
        var method = typeof(OpennessWorkerClient).GetMethod(methodName);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<WorkerCallResult>), method.ReturnType);
    }

    [Fact]
    public async Task OpenProjectRejectsUnconfirmedRequests()
    {
        var result = await ProjectLifecycleTools.OpenProject(
            workerClient: null!,
            projectPath: "C:\\Projects\\Line.ap21");

        Assert.Contains("Operation not confirmed", result);
        Assert.Contains("confirm=true", result);
    }

    [Fact]
    public async Task SaveProjectAsRejectsUnconfirmedRequests()
    {
        var result = await ProjectLifecycleTools.SaveProjectAs(
            workerClient: null!,
            targetDirectory: "C:\\Projects",
            targetName: "LineCopy");

        Assert.Contains("Operation not confirmed", result);
        Assert.Contains("confirm=true", result);
    }
}
