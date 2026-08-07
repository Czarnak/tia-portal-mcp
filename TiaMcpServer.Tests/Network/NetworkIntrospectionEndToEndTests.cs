using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OperationBatches;
using Xunit;

namespace TiaMcpServer.Tests.Network;

[Collection("Mcp protocol serial")]
public class NetworkIntrospectionEndToEndTests
{
    [Fact]
    public async Task PagedListResults_StayWholeAndUnderThePerItemBudget()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<NetworkReadTools>();
        var result = await CallReadAsync(
            harness,
            new object[]
            {
                new
                {
                    operationId = "page-1",
                    operation = "list_network_objects",
                    projectPath = "list-network-objects-large",
                    objectKinds = new[] { "node" },
                    pageSize = 20,
                },
                new
                {
                    operationId = "page-2",
                    operation = "list_network_objects",
                    projectPath = "list-network-objects-large",
                    objectKinds = new[] { "node" },
                    pageSize = 20,
                    cursor = "large-list-page-2",
                },
            });

        var root = AssertCanonical(result);
        Assert.False(result.IsError);
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal(2, items.GetArrayLength());
        foreach (var item in items.EnumerateArray())
        {
            Assert.Equal("succeeded", item.GetProperty("status").GetString());
            Assert.True(
                CanonicalJson.Serialize(item.GetProperty("result")).Length
                    < StructuredOperationBatchPayloadBudget.MaxItemChars);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("omission").ValueKind);
            Assert.Equal(20, item.GetProperty("result").GetProperty("returnedCount").GetInt32());
        }
    }

    [Fact]
    public async Task CursorFromEarlierFakeWorkerSnapshot_IsRejectedAfterSnapshotChanges()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<NetworkReadTools>();
        var before = await ReadStateMarkerAsync(harness);
        var after = await ReadStateMarkerAsync(harness);
        Assert.NotEqual(before, after);

        var queryHash = NetworkObjectCursorCodec.CreateQueryHash(new[] { NetworkObjectKinds.Node }, null);
        var beforeHash = NetworkObjectCursorCodec.CreateSnapshotHash(new[] { IndexedNode(before) });
        var afterHash = NetworkObjectCursorCodec.CreateSnapshotHash(new[] { IndexedNode(after) });
        var cursor = NetworkObjectCursorCodec.Encode(1, queryHash, beforeHash);

        var exception = Assert.Throws<NetworkCursorException>(
            () => NetworkObjectCursorCodec.Decode(cursor, queryHash, afterHash, totalCount: 1));

        Assert.Equal(WorkerFailureCategories.CursorSnapshotMismatch, exception.Category);
    }

    private static NetworkObjectSummaryInfo IndexedNode(string snapshotEvidence)
        => new()
        {
            Kind = NetworkObjectKinds.Node,
            Selectable = true,
            Selector = new NetworkObjectSelectorInfo
            {
                Kind = NetworkObjectKinds.Node,
                DeviceName = "PLC_2",
                NodeId = "node-1",
            },
            Evidence = new NetworkObjectEvidenceInfo
            {
                Name = "X1",
                NodeName = "X1",
                Address = snapshotEvidence,
            },
        };

    private static async Task<string> ReadStateMarkerAsync(McpProtocolTestHarness harness)
    {
        var result = await CallReadAsync(
            harness,
            new object[]
            {
                new
                {
                    operationId = "state",
                    operation = "read_hardware_config",
                    projectPath = "network-state-seq",
                },
            });
        var root = AssertCanonical(result);
        return root.GetProperty("batch")
            .GetProperty("operations")[0]
            .GetProperty("result")
            .GetProperty("messages")[0]
            .GetString()!;
    }

    private static ValueTask<CallToolResult> CallReadAsync(McpProtocolTestHarness harness, object operations)
        => harness.Client.CallToolAsync(
            "network_read",
            new Dictionary<string, object?> { ["operations"] = operations });

    private static JsonElement AssertCanonical(CallToolResult result)
    {
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal(CanonicalJson.Serialize(structured), text);
        using var parsed = JsonDocument.Parse(text);
        Assert.True(JsonElement.DeepEquals(structured, parsed.RootElement));
        return structured;
    }
}
