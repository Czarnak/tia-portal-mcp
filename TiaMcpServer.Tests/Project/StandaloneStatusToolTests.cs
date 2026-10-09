using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class StandaloneStatusToolTests
{
    internal static JsonElement Document(object response)
    {
        var result = Assert.IsType<CallToolResult>(response);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(text, structured.GetRawText());
        return structured;
    }

    [Fact]
    public async Task NoProject_StatusIsStructured_WithoutBindingAnUnboundSession()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, null, workerExecutablePath: FakeWorkerLocator.Locate());
        var root = Document(await ProjectReadTools.GetProjectStatus(client, "status-no-project"));
        Assert.Equal("get_project_status", root.GetProperty("tool").GetString());
        Assert.Equal("1.0", root.GetProperty("contractVersion").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var value = root.GetProperty("result").GetProperty("value");
        Assert.False(value.GetProperty("isOpen").GetBoolean());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("isModified").ValueKind);
        Assert.Equal(JsonValueKind.Null, value.GetProperty("metadata").ValueKind);
        Assert.Null(binding.BoundProjectPath);
        Assert.False(binding.IsVerified);
    }

    [Fact]
    public async Task StatusOversize_IsWholeValueOmission()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath("status-oversized");
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null), null,
            workerExecutablePath: FakeWorkerLocator.Locate());
        var response = await ProjectReadTools.GetProjectStatus(client, "status-oversized");
        var root = Document(response);
        Assert.False(((CallToolResult)(object)response).IsError);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var result = root.GetProperty("result");
        Assert.Equal("omitted", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("value").ValueKind);
        Assert.Equal("resultExceededItemCharLimit", result.GetProperty("omission").GetProperty("reason").GetString());
        Assert.True(result.GetProperty("omission").GetProperty("originalChars").GetInt32() > 60000);
        Assert.Equal("get_project_status", result.GetProperty("omission").GetProperty("retryTool").GetString());
    }
}
