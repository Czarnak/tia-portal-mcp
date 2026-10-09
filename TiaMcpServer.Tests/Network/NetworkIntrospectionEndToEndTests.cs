using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OpennessWorker.Openness.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

[Collection("Mcp protocol serial")]
public class NetworkIntrospectionEndToEndTests
{
    [Fact]
    public async Task Inspect_ForwardsEveryOwnerSegmentAndOptionalConstraintThroughIpc()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "echo");
        var operation = new NetworkOperationRequest { OperationId = "inspect", Operation = "inspect_network_object", ProjectPath = "echo",
            Target = new() { Kind = "node", DeviceName = "Station", NodeId = "E1", InterfaceName = "PROFINET interface_1", NodeIndex = 0,
                InterfacePath = new[] { new NetworkInterfacePathSegment { Name = "PLC_DP", PositionNumber = 1, TypeIdentifier = "OrderNumber:CPU" },
                    new NetworkInterfacePathSegment { Name = "PROFINET interface_1", PositionNumber = 32768 } } } };
        var result = await NetworkWorkerInvoker.InvokeReadAsync(client, operation);
        Assert.True(result.Success, result.Error);
        using var document = JsonDocument.Parse(result.Payload);
        var target = document.RootElement.GetProperty("networkObjectTarget");
        Assert.True(target.TryGetProperty("interfacePath", out var path));
        Assert.Equal(2, path.GetArrayLength());
        Assert.Equal("PLC_DP", path[0].GetProperty("name").GetString());
        Assert.Equal(1, path[0].GetProperty("positionNumber").GetInt32());
        Assert.Equal("OrderNumber:CPU", path[0].GetProperty("typeIdentifier").GetString());
        Assert.Equal("PROFINET interface_1", path[1].GetProperty("name").GetString());
        Assert.Equal(32768, path[1].GetProperty("positionNumber").GetInt32());
        Assert.False(path[1].TryGetProperty("typeIdentifier", out _));
        Assert.Equal("PROFINET interface_1", target.GetProperty("interfaceName").GetString());
        Assert.Equal(0, target.GetProperty("nodeIndex").GetInt32());
        Assert.Equal("E1", target.GetProperty("nodeId").GetString());
    }
    [Fact]
    public async Task TwoInterfacesWithE1_ReadListAndInspectRoundTripIndependently()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath("network-qualified-read");
        await using var harness = await McpProtocolTestHarness.StartAsync<NetworkReadTools>();
        var discovery = AssertCanonical(await CallReadAsync(harness, new object[]
        {
            new { operationId = "hardware", operation = "read_hardware_config", projectPath = "network-qualified-read" },
            new { operationId = "list", operation = "list_network_objects", projectPath = "network-qualified-read", objectKinds = new[] { "node" } },
        }));
        var operations = discovery.GetProperty("batch").GetProperty("operations");
        Assert.All(operations.EnumerateArray(), operation => Assert.Equal("succeeded", operation.GetProperty("status").GetString()));
        var owners = operations[0].GetProperty("result").GetProperty("devices")[0].GetProperty("items")[0].GetProperty("items");
        var list = operations[1].GetProperty("result").GetProperty("items");
        for (var index = 0; index < 2; index++)
        {
            var readSelector = owners[index].GetProperty("networkInterfaces")[0].GetProperty("nodes")[0].GetProperty("selector");
            Assert.Equal(CanonicalJson.Serialize(readSelector), CanonicalJson.Serialize(list[index].GetProperty("selector")));
            Assert.Equal(index == 0 ? 32768 : 33024, readSelector.GetProperty("interfacePath")[1].GetProperty("positionNumber").GetInt32());
            var inspected = AssertCanonical(await CallReadAsync(harness, new object[] { new
                { operationId = "inspect", operation = "inspect_network_object", projectPath = "network-qualified-read", target = readSelector } }));
            var item = inspected.GetProperty("batch").GetProperty("operations")[0];
            Assert.Equal("succeeded", item.GetProperty("status").GetString());
            Assert.Equal(index == 0 ? "X1" : "X2", item.GetProperty("result").GetProperty("evidence").GetProperty("nodeName").GetString());
            Assert.Equal(index == 0 ? "192.168.12.2" : "192.168.13.20", item.GetProperty("result").GetProperty("evidence").GetProperty("address").GetString());
            Assert.Equal(CanonicalJson.Serialize(readSelector), CanonicalJson.Serialize(item.GetProperty("result").GetProperty("target")));
        }
    }

    [Fact]
    public async Task PagedListResults_StayWholeAndUnderThePerItemBudget()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath("list-network-objects-large");
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
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath("network-state-seq");
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
