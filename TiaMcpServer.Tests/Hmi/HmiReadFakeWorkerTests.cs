using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Hmi;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

[Collection(RealWorkerProcessCollection.Name)]
public class HmiReadFakeWorkerTests
{
    private const string Scenario = "hmi-read-roundtrip";

    private static async Task<OpennessWorkerClient> CreateClientAsync(string scenario = Scenario)
    {
        var binding = new ProjectSessionBinding(null);
        var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadOnly));
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);
        return client;
    }

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
        using var client = await CreateClientAsync();

        var result = await HmiReadTools.HmiRead(client, new HmiPageCursorCodec(TiaMcpServer.Cursors.AuthenticatedCursorProtector.CreateProcessScoped()), new[]
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
            using var client = await CreateClientAsync();

            var result = await HmiReadTools.HmiRead(client, new HmiPageCursorCodec(TiaMcpServer.Cursors.AuthenticatedCursorProtector.CreateProcessScoped()), new[]
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

    private const string PagedScenario = "hmi-read-paged";

    private static HmiOperationRequest PagedOp(string? cursor = null)
        => new()
        {
            OperationId = "tags", Operation = "list_tags", ProjectPath = PagedScenario,
            HmiName = "HMI_1", Limit = 2, Cursor = cursor,
        };

    private static async Task<JsonElement> PageAsync(
        OpennessWorkerClient client, HmiPageCursorCodec codec, string? cursor = null)
    {
        var result = await HmiReadTools.HmiRead(client, codec, new[] { PagedOp(cursor) });
        return Items(result)[0];
    }

    private static string[] Names(JsonElement item)
        => item.GetProperty("result").GetProperty("tags").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()!).ToArray();

    [Fact]
    public async Task Paged_FirstContinuationLast_FollowsCursor()
    {
        using var client = await CreateClientAsync(PagedScenario);
        var codec = new HmiPageCursorCodec(TiaMcpServer.Cursors.AuthenticatedCursorProtector.CreateProcessScoped());

        var first = await PageAsync(client, codec);
        var second = await PageAsync(client, codec, first.GetProperty("result").GetProperty("page").GetProperty("nextCursor").GetString());
        var last = await PageAsync(client, codec, second.GetProperty("result").GetProperty("page").GetProperty("nextCursor").GetString());

        Assert.Equal(new[] { "1", "2" }, Names(first));
        Assert.Equal(new[] { "3", "4" }, Names(second));
        Assert.Equal(new[] { "5" }, Names(last));
        Assert.False(last.GetProperty("result").GetProperty("page").TryGetProperty("nextCursor", out _));
    }

    [Fact]
    public async Task Paged_UnknownSnapshot_IsSnapshotUnavailable()
    {
        using var client = await CreateClientAsync(PagedScenario);
        var codec = new HmiPageCursorCodec(TiaMcpServer.Cursors.AuthenticatedCursorProtector.CreateProcessScoped());

        var first = await PageAsync(client, codec);
        var issued = codec.Decode(first.GetProperty("result").GetProperty("page").GetProperty("nextCursor").GetString()!);
        var unknown = codec.Encode(issued with { SnapshotId = "s2" });

        var item = await PageAsync(client, codec, unknown);

        Assert.Equal("failed", item.GetProperty("status").GetString());
        Assert.Equal("snapshot_unavailable", item.GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public async Task ProtocolMismatchPayloadIsProtocolError()
    {
        using var client = await CreateClientAsync();

        var result = await HmiReadTools.HmiRead(client, new HmiPageCursorCodec(TiaMcpServer.Cursors.AuthenticatedCursorProtector.CreateProcessScoped()), new[] { Op("sys", "list_system_tags", r => r.HmiName = "HMI_1") });

        var item = Items(result)[0];
        Assert.Equal("failed", item.GetProperty("status").GetString());
        Assert.Equal("protocol_error", item.GetProperty("failure").GetProperty("category").GetString());
        Assert.DoesNotContain("SENTINEL-PAYLOAD", item.GetRawText());
    }
}
