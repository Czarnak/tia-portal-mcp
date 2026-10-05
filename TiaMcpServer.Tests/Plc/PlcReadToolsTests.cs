using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

[Collection(RealWorkerProcessCollection.Name)]
public class PlcReadToolsTests
{
    private const string Scenario = "plc-read-roundtrip";

    private static OpennessWorkerClient CreateClient(McpAccessMode mode = McpAccessMode.ReadOnly)
        => new(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(mode));

    private static PlcOperationRequest Block(string id, string path = "PLC_1/Main", string? projectPath = Scenario) => new()
    {
        OperationId = id,
        Operation = "get_block_content",
        BlockPath = path,
        ProjectPath = projectPath,
    };

    private static JsonElement Structured(CallToolResult result) => Assert.IsType<JsonElement>(result.StructuredContent);

    [Fact]
    public async Task FailedItemDoesNotStopLaterItems()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(Scenario);
        using var client = CreateClient();

        var result = await PlcReadTools.PlcRead(client, new[]
        {
            Block("missing", "PLC_1/Missing"),
            Block("ok"),
            new PlcOperationRequest { OperationId = "tags", Operation = "list_tag_tables", ProjectPath = Scenario },
        });

        Assert.False(result.IsError);
        var root = Structured(result);
        Assert.Equal("plc_read", root.GetProperty("tool").GetString());
        Assert.Equal("1.0", root.GetProperty("contractVersion").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("warnings").ValueKind);
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal("failed", items[0].GetProperty("status").GetString());
        Assert.Equal("succeeded", items[1].GetProperty("status").GetString());
        Assert.Equal("PLC_1_Device", items[2].GetProperty("result").GetProperty("plcs")[0].GetProperty("deviceName").GetString());
    }

    [Fact]
    public async Task ListTagTablesForwardsTableNameAndFolderPath()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(Scenario);
        using var client = CreateClient();

        var result = await PlcReadTools.PlcRead(client, new[]
        {
            new PlcOperationRequest
            {
                OperationId = "tags", Operation = "list_tag_tables", ProjectPath = Scenario,
                TableName = "Motors", FolderPath = "/Line",
            },
        });

        var table = Structured(result).GetProperty("batch").GetProperty("operations")[0]
            .GetProperty("result").GetProperty("plcs")[0].GetProperty("tables")[0];
        Assert.Equal("Motors", table.GetProperty("name").GetString());
        Assert.Equal("/Line", table.GetProperty("folderPath").GetString());
    }

    [Fact]
    public async Task MismatchedProjectPathFailsOnlyThatItem()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(Scenario);
        using var client = CreateClient();

        var result = await PlcReadTools.PlcRead(client, new[]
        {
            Block("first"),
            Block("other", projectPath: @"C:\Somewhere\Else\Other.ap21"),
            Block("last"),
        });

        Assert.False(result.IsError);
        var items = Structured(result).GetProperty("batch").GetProperty("operations");
        Assert.Equal("succeeded", items[0].GetProperty("status").GetString());
        Assert.Equal("failed", items[1].GetProperty("status").GetString());
        Assert.Equal("binding_conflict", items[1].GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal("succeeded", items[2].GetProperty("status").GetString());
    }

    [Fact]
    public async Task ValidationFailureIsErrorEnvelope()
    {
        using var client = CreateClient();

        var result = await PlcReadTools.PlcRead(client, new[] { Block("a"), Block("a") });

        Assert.True(result.IsError);
        var root = Structured(result);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("validation_error", root.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("batch").ValueKind);
    }

    [Fact]
    public async Task TextEqualsStructuredContent()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(Scenario);
        using var client = CreateClient();

        var result = await PlcReadTools.PlcRead(client, new[] { Block("ok") });

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal(text, Structured(result).GetRawText());
        using var doc = JsonDocument.Parse(text);
        var content = doc.RootElement.GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Equal(JsonValueKind.Object, content.ValueKind);
    }
}
