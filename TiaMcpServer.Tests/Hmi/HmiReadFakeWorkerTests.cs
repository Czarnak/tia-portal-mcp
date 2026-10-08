using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Hmi;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

[Collection(RealWorkerProcessCollection.Name)]
public class HmiReadFakeWorkerTests
{
    private const string Scenario = "hmi-read-roundtrip";

    private static OpennessWorkerClient CreateClient()
        => new(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadOnly));

    private static HmiOperationRequest Op(string id, string operation, Action<HmiOperationRequest>? configure = null)
    {
        var request = new HmiOperationRequest { OperationId = id, Operation = operation, ProjectPath = Scenario };
        configure?.Invoke(request);
        return request;
    }

    private static JsonElement Items(CallToolResult result)
        => Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("batch").GetProperty("operations");

    [Fact]
    public async Task MixedBatchRoundTripsTypedItemsAndOneFailure()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(Scenario);
        using var client = CreateClient();

        var result = await HmiReadTools.HmiRead(client, new[]
        {
            Op("devices", "list_hmi_devices"),
            Op("tags", "list_tags", r => r.HmiName = "HMI_1"),
            Op("missing", "get_tag", r => { r.HmiName = "HMI_1"; r.TagName = "Missing"; }),
        });

        Assert.False(result.IsError);
        var root = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.False(root.GetProperty("success").GetBoolean());
        var items = Items(result);
        Assert.Equal("succeeded", items[0].GetProperty("status").GetString());
        Assert.Equal("HMI_1", items[0].GetProperty("result").GetProperty("devices")[0].GetProperty("deviceName").GetString());
        Assert.Equal("succeeded", items[1].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Object, items[1].GetProperty("result").GetProperty("page").ValueKind);
        Assert.Equal("failed", items[2].GetProperty("status").GetString());
        Assert.Equal("target_not_found", items[2].GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public async Task WorkerReceivesPrefixedMethodAndQuery()
    {
        var log = Path.Combine(Path.GetTempPath(), $"hmi-requests-{Guid.NewGuid():N}.log");
        var prior = Environment.GetEnvironmentVariable("TIA_MCP_FAKE_WORKER_REQUEST_LOG");
        Environment.SetEnvironmentVariable("TIA_MCP_FAKE_WORKER_REQUEST_LOG", log);
        try
        {
            using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(Scenario);
            using var client = CreateClient();

            var result = await HmiReadTools.HmiRead(client, new[]
            {
                Op("tags", "list_tags", r => { r.HmiName = "HMI_1"; r.TableName = "Motors"; r.Offset = 5; r.Limit = 7; }),
            });

            Assert.Contains("hmi_list_tags", await File.ReadAllLinesAsync(log));
            var name = Items(result)[0].GetProperty("result").GetProperty("tags")[0].GetProperty("name").GetString();
            Assert.Equal("HMI_1|Motors|5|7", name);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIA_MCP_FAKE_WORKER_REQUEST_LOG", prior);
            File.Delete(log);
        }
    }

    [Fact]
    public async Task ProtocolMismatchPayloadIsProtocolError()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(Scenario);
        using var client = CreateClient();

        var result = await HmiReadTools.HmiRead(client, new[] { Op("sys", "list_system_tags", r => r.HmiName = "HMI_1") });

        var item = Items(result)[0];
        Assert.Equal("failed", item.GetProperty("status").GetString());
        Assert.Equal("protocol_error", item.GetProperty("failure").GetProperty("category").GetString());
        Assert.DoesNotContain("SENTINEL-PAYLOAD", item.GetRawText());
    }
}
