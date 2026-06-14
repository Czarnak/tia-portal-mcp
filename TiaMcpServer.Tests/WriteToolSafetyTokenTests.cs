using System.Reflection;
using ModelContextProtocol.Server;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests;

public class WriteToolSafetyTokenTests
{
    [Theory]
    [InlineData(typeof(UpdateBlockLogicTool), "PreviewUpdateBlockLogic", "preview_update_block_logic")]
    [InlineData(typeof(TagTableOperationsTool), "PreviewCreateTagTable", "preview_create_tag_table")]
    [InlineData(typeof(TagTableOperationsTool), "PreviewDeleteTagTable", "preview_delete_tag_table")]
    [InlineData(typeof(TagOperationsTool), "PreviewCreateTag", "preview_create_tag")]
    [InlineData(typeof(TagOperationsTool), "PreviewUpdateTag", "preview_update_tag")]
    [InlineData(typeof(TagOperationsTool), "PreviewDeleteTag", "preview_delete_tag")]
    [InlineData(typeof(UserConstantOperationsTool), "PreviewCreateUserConstant", "preview_create_user_constant")]
    [InlineData(typeof(UserConstantOperationsTool), "PreviewUpdateUserConstant", "preview_update_user_constant")]
    [InlineData(typeof(UserConstantOperationsTool), "PreviewDeleteUserConstant", "preview_delete_user_constant")]
    [InlineData(typeof(AddNetworkDeviceTool), "PreviewAddNetworkDevice", "preview_add_network_device")]
    [InlineData(typeof(ConfigureNetworkDeviceTool), "PreviewConfigureNetworkDevice", "preview_configure_network_device")]
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
    public async Task ConfirmedBlockUpdateRejectsMissingSafetyToken()
    {
        var result = await UpdateBlockLogicTool.UpdateBlockLogic(
            workerClient: null!,
            blockPath: "PLC_1/Main",
            yamlContent: "name: Main",
            confirm: true);

        Assert.Contains("Safety token required", result);
        Assert.Contains("preview_update_block_logic", result);
    }

    [Fact]
    public async Task ConfirmedDeviceAddRejectsMissingSafetyToken()
    {
        var result = await AddNetworkDeviceTool.AddNetworkDevice(
            workerClient: null!,
            typeIdentifier: "OrderNumber:6ES7 510-1DJ01-0AB0/V2.0",
            deviceName: "PLC_1",
            confirm: true);

        Assert.Contains("Safety token required", result);
        Assert.Contains("preview_add_network_device", result);
    }

    [Fact]
    public async Task ConfirmedTagUpdateRejectsMissingSafetyToken()
    {
        var result = await TagOperationsTool.UpdateTag(
            workerClient: null!,
            tableName: "Inputs",
            name: "StartButton",
            confirm: true);

        Assert.Contains("Safety token required", result);
        Assert.Contains("preview_update_tag", result);
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
