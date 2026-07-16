using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests;

public class WriteToolSafetyTokenTests
{
    [Theory]
    [InlineData("PreviewOpenProject")]
    [InlineData("PreviewCreateProject")]
    [InlineData("PreviewSaveProject")]
    [InlineData("PreviewSaveProjectAs")]
    [InlineData("PreviewArchiveProject")]
    [InlineData("PreviewCloseProject")]
    public void SeparatePreviewToolsAreGone(string methodName)
    {
        Assert.Null(typeof(ProjectLifecycleTools).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    public void LifecycleSurfaceIsExactlySevenTools()
    {
        var toolNames = typeof(ProjectLifecycleTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => name is not null)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "archive_project", "close_project", "create_project", "get_project_status",
                "open_project", "save_project", "save_project_as"
            },
            toolNames);
    }

    [Fact]
    public async Task WriteToolWithoutToken_ReturnsPreviewWithTokenAndInstructions()
    {
        var result = await ProjectLifecycleTools.OpenProject(
            workerClient: null!,
            projectPath: "C:\\Projects\\Line.ap21");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("open_project", doc.RootElement.GetProperty("toolName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("safetyToken").GetString()));
        Assert.Contains("confirm=true", doc.RootElement.GetProperty("instructions").GetString());
    }

    [Fact]
    public async Task WriteToolWithTokenButNoConfirm_RejectsBeforeAnyWork()
    {
        var result = await ProjectLifecycleTools.CloseProject(
            workerClient: null!,
            confirm: false,
            safetyToken: "some-token");

        Assert.Contains("confirm=true", result);
        Assert.Contains("without safetyToken", result);
    }

    [Fact]
    public async Task WriteToolWithBadToken_PointsBackAtTheTokenlessCall()
    {
        var result = await ProjectLifecycleTools.OpenProject(
            workerClient: null!,
            projectPath: "C:\\Projects\\Line.ap21",
            confirm: true,
            safetyToken: "bogus-token");

        Assert.Contains("Safety token", result);
        Assert.Contains("open_project (without safetyToken)", result);
    }
}
