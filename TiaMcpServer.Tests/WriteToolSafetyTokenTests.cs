using System.Reflection;
using ModelContextProtocol.Server;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests;

public class WriteToolSafetyTokenTests
{
    [Theory]
    [InlineData(typeof(ProjectLifecycleTools), "PreviewOpenProject", "preview_open_project")]
    [InlineData(typeof(ProjectLifecycleTools), "PreviewCreateProject", "preview_create_project")]
    [InlineData(typeof(ProjectLifecycleTools), "PreviewSaveProject", "preview_save_project")]
    [InlineData(typeof(ProjectLifecycleTools), "PreviewSaveProjectAs", "preview_save_project_as")]
    [InlineData(typeof(ProjectLifecycleTools), "PreviewArchiveProject", "preview_archive_project")]
    [InlineData(typeof(ProjectLifecycleTools), "PreviewCloseProject", "preview_close_project")]
    public void PreviewToolsHaveMcpMetadata(Type toolType, string methodName, string expectedToolName)
    {
        var method = toolType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        var toolAttribute = method.GetCustomAttribute<McpServerToolAttribute>();
        Assert.NotNull(toolAttribute);
        Assert.Equal(expectedToolName, toolAttribute.Name);
    }

    [Fact]
    public async Task ConfirmedProjectCloseRejectsMissingSafetyToken()
    {
        var result = await ProjectLifecycleTools.CloseProject(
            workerClient: null!,
            confirm: true);

        Assert.Contains("Safety token required", result);
        Assert.Contains("preview_close_project", result);
    }
}
