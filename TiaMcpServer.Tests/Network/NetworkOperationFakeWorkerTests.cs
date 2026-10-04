using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Offline FakeWorker evidence for dedicated Network reads and guarded writes, exact ordered
/// requests and typed payloads without nested JSON strings. Previews use explicit dryRun:true;
/// actual calls re-plan and verify under the pinned binding without Network tokens.
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public class NetworkOperationFakeWorkerTests
{
    private const string Scenario = "network-roundtrip";

    private static OpennessWorkerClient CreateClient(ProjectSessionBinding? binding = null)
        => new(
            binding ?? new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

    private static OpennessWorkerClient CreateWriteClient(
        TempAuditDirectory audit,
        out WriteExecution safety,
        string projectPath)
    {
        var binding = new ProjectSessionBinding(null);
        var client = CreateClient(binding);
        NetworkVerifiedWriteFixture.VerifyAsync(client, binding, projectPath).GetAwaiter().GetResult();
        safety = NetworkGuardedWriteFixture.CreateRunner(client, audit.Path);
        return client;
    }

    private static NetworkOperationRequest ReadHardware(string operationId) => new()
    {
        OperationId = operationId,
        Operation = "read_hardware_config",
        ProjectPath = Scenario,
    };

    private static NetworkOperationRequest SearchCatalog(string operationId) => new()
    {
        OperationId = operationId,
        Operation = "search_equipment_catalog",
        ProjectPath = Scenario,
        Query = "OrderNumber:TEST",
    };

    private static NetworkOperationRequest AddDevice(string operationId) => new()
    {
        OperationId = operationId,
        Operation = "add_network_device",
        ProjectPath = Scenario,
        TypeIdentifier = "OrderNumber:TEST",
        DeviceName = "AddedPLC",
    };

    private static NetworkOperationRequest ConfigureDevice(string operationId, string ipAddress = "192.168.0.10") => new()
    {
        OperationId = operationId,
        Operation = "configure_network_device",
        ProjectPath = Scenario,
        Target = new NetworkObjectTarget { DeviceName = "PLC_1", NodeId = "node-1" },
        Changes = new NetworkDeviceChanges { IpAddress = ipAddress },
    };

    private static NetworkOperationRequest QualifiedConfigure(string id, int position = 32768) => new()
    {
        OperationId = id, Operation = "configure_network_device", Target = new() { Kind = "node", DeviceName = "S7-1500/ET200MP station_1", NodeId = "E1",
            InterfacePath = new[] { new NetworkInterfacePathSegment { Name = "PLC_DP", PositionNumber = 1 },
                new NetworkInterfacePathSegment { Name = position == 32768 ? "PROFINET interface_1" : "PROFINET interface_2", PositionNumber = position } } },
        Changes = new() { IpAddress = "192.168.12.99" }
    };

    [Fact]
    public async Task PreparedDomainRequest_DetachesCallerOwnerBeforeReplanAndDispatch()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-qualified-read");
        var operation = QualifiedConfigure("prepare");
        var domain = new NetworkWriteDomain(fixture.Client);
        Assert.True((await domain.PlanAsync("network-qualified-read", new[] { operation })).Success);
        operation.Target!.InterfacePath![1].Name = "PROFINET interface_2";
        operation.Target.InterfacePath[1].PositionNumber = 33024;
        Assert.True((await domain.ReplanAsync("network-qualified-read", operation)).Success);
        var result = await domain.MutateAsync("network-qualified-read", operation);
        Assert.Equal("succeeded", domain.Project(operation, result).Status);
        var state = (await NetworkWritePlanner.ReadCurrentStateAsync(fixture.Client, "network-qualified-read")).State!;
        Assert.Equal("192.168.12.99", state.Devices[0].Items[0].Items[0].NetworkInterfaces[0].Nodes[0].IpAddress);
        Assert.Equal("192.168.13.20", state.Devices[0].Items[0].Items[1].NetworkInterfaces[0].Nodes[0].IpAddress);
    }

    [Fact]
    public async Task ConfigureX1_DoesNotChangeX2()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-qualified-read");
        var operation = QualifiedConfigure("X1");
        var result = await NetworkWorkerInvoker.InvokeWriteAsync(fixture.Client, operation, "network-qualified-read");
        Assert.True(result.Success, result.Error);
        Assert.Equal("succeeded", NetworkPayloadContract.Project(operation, result, true).Status);
        var snapshot = await NetworkWritePlanner.ReadCurrentStateAsync(fixture.Client, "network-qualified-read");
        Assert.True(snapshot.Success, snapshot.Error);
        var owners = snapshot.State!.Devices[0].Items[0].Items;
        Assert.Equal("192.168.12.99", owners[0].NetworkInterfaces[0].Nodes[0].IpAddress);
        Assert.Equal("192.168.13.20", owners[1].NetworkInterfaces[0].Nodes[0].IpAddress);
        var second = QualifiedConfigure("X2", 33024); second.Changes = new() { IpAddress = "192.168.13.99" };
        Assert.True((await NetworkWorkerInvoker.InvokeWriteAsync(fixture.Client, second, "network-qualified-read")).Success);
        snapshot = await NetworkWritePlanner.ReadCurrentStateAsync(fixture.Client, "network-qualified-read");
        owners = snapshot.State!.Devices[0].Items[0].Items;
        Assert.Equal("192.168.12.99", owners[0].NetworkInterfaces[0].Nodes[0].IpAddress);
        Assert.Equal("192.168.13.99", owners[1].NetworkInterfaces[0].Nodes[0].IpAddress);
    }

    [Fact]
    public async Task QualifiedPartialSkip_RetainsAppliedIdentityAndStops()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-qualified-partial");
        var first = QualifiedConfigure("partial"); first.Changes = new() { IpAddress = "192.168.12.99", SubnetMask = "255.255.255.0" };
        var response = await fixture.RunAsync(false, first, QualifiedConfigure("later", 33024));
        Assert.False(response.Success); Assert.Null(response.Error);
        var result = response.Batch!.Operations[0].Result!.Value;
        Assert.Equal("Address", Assert.Single(result.GetProperty("appliedSettings").EnumerateObject()).Name);
        Assert.Equal("SubnetMask", Assert.Single(result.GetProperty("skippedSettings").EnumerateObject()).Name);
        Assert.Contains("32768", result.GetProperty("verification").GetProperty("identity").GetProperty("interfacePath").GetString());
        Assert.Equal("earlierOperationFailed", response.Batch.Operations[1].SkipReason);
        var snapshot = await NetworkWritePlanner.ReadCurrentStateAsync(fixture.Client, "network-qualified-partial");
        Assert.Equal("192.168.13.20", snapshot.State!.Devices[0].Items[0].Items[1].NetworkInterfaces[0].Nodes[0].IpAddress);
    }

    [Fact]
    public async Task QualifiedUnknownDependency_RefusesBeforeScalarSetter()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-qualified-read");
        var operation = QualifiedConfigure("unknown"); operation.Changes = new() { IpAddress = "192.168.12.99", IoSystem = new() { SubnetId = "missing", Number = 1 } };
        var result = await NetworkWorkerInvoker.InvokeWriteAsync(fixture.Client, operation, "network-qualified-read");
        Assert.False(result.Success);
        var snapshot = await NetworkWritePlanner.ReadCurrentStateAsync(fixture.Client, "network-qualified-read");
        Assert.Equal("192.168.12.2", snapshot.State!.Devices[0].Items[0].Items[0].NetworkInterfaces[0].Nodes[0].IpAddress);
    }

    [Theory]
    [InlineData("network-config-partial", true)]
    [InlineData("network-config-all-skipped", false)]
    public async Task NetworkWrite_CompletedSparseConfigurationFailureRetainsResultAndSkipsLaterWrites(
        string scenario,
        bool addressApplied)
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateWriteClient(audit, out var safety, scenario);
        var operations = new[]
        {
            new NetworkOperationRequest
            {
                OperationId = "configure",
                Operation = "configure_network_device",
                ProjectPath = scenario,
                Target = new NetworkObjectTarget { DeviceName = "PLC_1", NodeId = "node-1" },
                Changes = new NetworkDeviceChanges
                {
                    IpAddress = "192.168.0.10",
                    IoSystem = new NetworkIoSystemTarget { SubnetId = "subnet-1", Number = 100 },
                },
            },
            new NetworkOperationRequest
            {
                OperationId = "later",
                Operation = "add_network_device",
                ProjectPath = scenario,
                TypeIdentifier = "OrderNumber:TEST",
                DeviceName = "LaterDevice",
            },
        };

        var applied = await NetworkWriteTools.NetworkWrite(client, safety, operations, dryRun: false);

        Assert.False(applied.IsError, Text(applied));
        var root = Structured(applied);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal("failed", items[0].GetProperty("status").GetString());
        Assert.Equal("worker_operation_failed", items[0].GetProperty("failure").GetProperty("category").GetString());
        var result = items[0].GetProperty("result");
        Assert.Equal("PLC_1", result.GetProperty("deviceName").GetString());
        Assert.Equal(
            addressApplied ? new[] { "Address" } : Array.Empty<string>(),
            result.GetProperty("appliedSettings").EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            addressApplied ? new[] { "IoSystem" } : new[] { "Address", "IoSystem" },
            result.GetProperty("skippedSettings").EnumerateObject().Select(property => property.Name));
        Assert.StartsWith("seq:", result.GetProperty("messages")[0].GetString());
        Assert.Equal("skipped", items[1].GetProperty("status").GetString());
        Assert.Equal("earlierOperationFailed", items[1].GetProperty("skipReason").GetString());
    }

    [Fact]
    public async Task NetworkRead_HardwareAndCatalogSucceedInRequestedOrderWithDeclaredJsonResults()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(Scenario);
        using var client = CreateClient();

        var result = await NetworkReadTools.NetworkRead(
            client,
            new[] { ReadHardware("hardware"), SearchCatalog("catalog") });

        Assert.False(result.IsError, Text(result));
        var root = Assert.IsType<JsonElement>(result.StructuredContent);
        var operations = root.GetProperty("batch").GetProperty("operations");

        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("hardware", operations[0].GetProperty("operationId").GetString());
        Assert.Equal("succeeded", operations[0].GetProperty("status").GetString());
        Assert.Equal(
            "PLC_1",
            operations[0].GetProperty("result").GetProperty("devices")[0].GetProperty("name").GetString());
        Assert.Equal("catalog", operations[1].GetProperty("operationId").GetString());
        Assert.Equal("succeeded", operations[1].GetProperty("status").GetString());
        Assert.Equal(
            "OrderNumber:TEST",
            operations[1].GetProperty("result")[0].GetProperty("typeIdentifier").GetString());
    }

    /// <summary>
    /// The scenario's hardware payload models a multi-homed PC station: one station, two
    /// interfaces, one node each. Both nodes must survive the strict contract with different node
    /// ids so a later selector can address either interface without touching the other.
    /// </summary>
    [Fact]
    public async Task NetworkRead_MultiHomedPcStationExposesBothNodesWithDistinctNodeIds()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(Scenario);
        using var client = CreateClient();

        var result = await NetworkReadTools.NetworkRead(client, new[] { ReadHardware("hardware") });

        Assert.False(result.IsError, Text(result));
        var hardware = Assert.IsType<JsonElement>(result.StructuredContent)
            .GetProperty("batch")
            .GetProperty("operations")[0]
            .GetProperty("result");

        var station = hardware.GetProperty("devices")[1];
        Assert.Equal("PC_System_1", station.GetProperty("name").GetString());

        var nodes = station.GetProperty("items")
            .EnumerateArray()
            .SelectMany(item => item.GetProperty("networkInterfaces").EnumerateArray())
            .SelectMany(networkInterface => networkInterface.GetProperty("nodes").EnumerateArray())
            .ToList();

        Assert.Equal(2, nodes.Count);
        Assert.Equal(new[] { "0", "1" }, nodes.Select(node => node.GetProperty("nodeId").GetString()));
        Assert.Equal("192.168.0.20", nodes[0].GetProperty("ipAddress").GetString());
        Assert.Equal("10.0.0.20", nodes[1].GetProperty("ipAddress").GetString());

        var subnet = hardware.GetProperty("subnets")[0];
        Assert.Equal("subnet-1", subnet.GetProperty("subnetId").GetString());
        Assert.Equal("Ethernet", subnet.GetProperty("networkType").GetString());
        Assert.Equal(100, subnet.GetProperty("ioSystems")[0].GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task NetworkWrite_ExecutesInCallerOrderWithTypedResults()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateWriteClient(audit, out var safety, Scenario);
        var operations = new[] { AddDevice("add"), ConfigureDevice("configure") };

        var preview = await NetworkWriteTools.NetworkWrite(client, safety, operations, dryRun: true);

        var applied = await NetworkWriteTools.NetworkWrite(client, safety, operations, dryRun: false);
        var results = Structured(applied).GetProperty("batch").GetProperty("operations");
        Assert.True(Structured(applied).GetProperty("success").GetBoolean(), Text(applied));
        Assert.Equal("add", results[0].GetProperty("operationId").GetString());
        Assert.Equal("succeeded", results[0].GetProperty("status").GetString());

        // Each result is the declared contract type as JSON, never a nested JSON string.
        // Typed identities and outcomes below establish the fixture result, not a fixed request sequence.
        Assert.Equal(JsonValueKind.Object, results[0].GetProperty("result").ValueKind);
        Assert.Equal("AddedPLC", results[0].GetProperty("result").GetProperty("deviceName").GetString());
        Assert.Equal("configure", results[1].GetProperty("operationId").GetString());
        Assert.Equal("succeeded", results[1].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Object, results[1].GetProperty("result").ValueKind);
        Assert.Equal("PLC_1", results[1].GetProperty("result").GetProperty("deviceName").GetString());


    }

    /// <summary>
    /// Duplicate node IDs on one device fail the exact-one selector before mutation. This is
    /// offline evidence through NetworkWriteTools/FakeWorker, not live TIA qualification.
    /// </summary>
    [Fact]
    public async Task NetworkWrite_AmbiguousNodeIdFailsClosedWithNoTokenIssued()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateWriteClient(audit, out var safety, "network-ambiguous-node");

        var result = await NetworkWriteTools.NetworkWrite(
            client,
            safety,
            new[]
            {
                new NetworkOperationRequest
                {
                    OperationId = "configure",
                    Operation = "configure_network_device",
                    ProjectPath = "network-ambiguous-node",
                    Target = new NetworkObjectTarget { DeviceName = "PC_1", NodeId = "dup-node" },
                    Changes = new NetworkDeviceChanges { IpAddress = "192.168.0.55" },
                },
            });

        var root = Structured(result);
        Assert.True(result.IsError);
        Assert.Equal("error", root.GetProperty("phase").GetString());
        Assert.Equal(
            WorkerFailureCategories.PostconditionFailed,
            root.GetProperty("error").GetProperty("category").GetString());
    }

    /// <summary>
    /// changes.subnet and changes.ioSystem naming different subnets describes an outcome that
    /// cannot exist (a node connects to one subnet). NetworkOperationCatalog rejects this before
    /// the request ever resolves a target or reaches the worker - proven here through the actual
    /// NetworkWriteTools entry point, not only the catalog validator in isolation.
    /// </summary>
    [Fact]
    public async Task NetworkWrite_MismatchedSubnetAndIoSystemPairingIsRejectedBeforeAnyWorkerCall()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateClient();
        var safety = NetworkGuardedWriteFixture.CreateRunner(client, audit.Path);

        var result = await NetworkWriteTools.NetworkWrite(
            client,
            safety,
            new[]
            {
                new NetworkOperationRequest
                {
                    OperationId = "configure",
                    Operation = "configure_network_device",
                    ProjectPath = Scenario,
                    Target = new NetworkObjectTarget { DeviceName = "PLC_1", NodeId = "node-1" },
                    Changes = new NetworkDeviceChanges
                    {
                        Subnet = new NetworkSubnetTarget { SubnetId = "subnet-a" },
                        IoSystem = new NetworkIoSystemTarget { SubnetId = "subnet-b", Number = 100 },
                    },
                },
            });

        Assert.True(result.IsError);
        Assert.Contains("must name the same subnet", Text(result));
    }

    // --- Phase 3 read round-trips -----------------------------------------------

    [Fact]
    public async Task ListNetworkObjects_RoundTripReturnsSixItemsWithCommunicationConnectionUnselectable()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath("list-network-objects-success");
        using var client = CreateClient();

        var result = await NetworkReadTools.NetworkRead(
            client,
            new[]
            {
                new NetworkOperationRequest
                {
                    OperationId = "list-1",
                    Operation = "list_network_objects",
                    ProjectPath = "list-network-objects-success",
                    ObjectKinds = new[] { "node", "communicationConnection" },
                },
            });

        Assert.False(result.IsError, Text(result));
        var operation = Structured(result)
            .GetProperty("batch")
            .GetProperty("operations")[0];

        Assert.Equal("succeeded", operation.GetProperty("status").GetString());

        // Result is declared JSON, not a nested JSON string.
        var resultElement = operation.GetProperty("result");
        Assert.Equal(JsonValueKind.Object, resultElement.ValueKind);

        var items = resultElement.GetProperty("items");
        Assert.Equal(6, items.GetArrayLength());
        Assert.Equal(6, resultElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, resultElement.GetProperty("nextCursor").ValueKind);

        // communicationConnection (index 5) has null selector — it's unselectable.
        var connection = items[5];
        Assert.Equal("communicationConnection", connection.GetProperty("kind").GetString());
        Assert.False(connection.GetProperty("selectable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, connection.GetProperty("selector").ValueKind);
        Assert.Single(connection.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public async Task InspectNetworkObject_RoundTripReturnsNineAttributesAndEvidence()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath("inspect-network-object-success");
        using var client = CreateClient();

        var result = await NetworkReadTools.NetworkRead(
            client,
            new[]
            {
                new NetworkOperationRequest
                {
                    OperationId = "inspect-1",
                    Operation = "inspect_network_object",
                    ProjectPath = "inspect-network-object-success",
                    Target = new NetworkObjectTarget { Kind = "node", DeviceName = "PLC_1", NodeId = "node-1" },
                },
            });

        Assert.False(result.IsError, Text(result));
        var operation = Structured(result)
            .GetProperty("batch")
            .GetProperty("operations")[0];

        Assert.Equal("succeeded", operation.GetProperty("status").GetString());

        var resultElement = operation.GetProperty("result");
        Assert.Equal(JsonValueKind.Object, resultElement.ValueKind);
        var target = resultElement.GetProperty("target");
        Assert.Equal("node", target.GetProperty("kind").GetString());
        Assert.Equal("PLC_1", target.GetProperty("deviceName").GetString());
        Assert.Equal("node-1", target.GetProperty("nodeId").GetString());

        // Evidence is a real object (not null, not a nested string).
        var evidence = resultElement.GetProperty("evidence");
        Assert.Equal(JsonValueKind.Object, evidence.ValueKind);
        Assert.Equal("X1", evidence.GetProperty("nodeName").GetString());
        Assert.Equal("Ethernet", evidence.GetProperty("nodeType").GetString());
        Assert.Equal(JsonValueKind.Array, evidence.GetProperty("deviceItemPath").ValueKind);

        var attrs = resultElement.GetProperty("attributes");
        Assert.Equal(9, attrs.GetArrayLength());

        // Spot-check known attributes. value is a typed object {kind, value}; navigate into it.
        var stringAttr = attrs.EnumerateArray().First(a => a.GetProperty("name").GetString() == "stringAttribute");
        Assert.Equal("192.168.0.10", stringAttr.GetProperty("value").GetProperty("value").GetString());

        var nullAttr = attrs.EnumerateArray().First(a => a.GetProperty("name").GetString() == "nullAttribute");
        var nullValue = nullAttr.GetProperty("value");
        Assert.Equal(JsonValueKind.Object, nullValue.ValueKind);
        Assert.Equal("null", nullValue.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, nullValue.GetProperty("value").ValueKind);

        var unknownAttr = attrs.EnumerateArray().First(a => a.GetProperty("name").GetString() == "unknownAttribute");
        Assert.Equal(
            "unknown_attribute",
            unknownAttr.GetProperty("diagnostic").GetProperty("category").GetString());
    }

    [Theory]
    [InlineData("list_network_objects", "list-network-objects-malformed")]
    [InlineData("inspect_network_object", "inspect-network-object-malformed")]
    public async Task Phase3Read_MalformedWorkerPayloadBecomesProtocolError(
        string operationName,
        string scenario)
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(scenario);
        using var client = CreateClient();
        var target = operationName == "inspect_network_object"
            ? new NetworkObjectTarget { Kind = "node", DeviceName = "PLC_1", NodeId = "node-1" }
            : null;
        var objectKinds = operationName == "list_network_objects"
            ? new[] { "node" }
            : null;

        var result = await NetworkReadTools.NetworkRead(
            client,
            new[]
            {
                new NetworkOperationRequest
                {
                    OperationId = "malformed-1",
                    Operation = operationName,
                    ProjectPath = scenario,
                    Target = target,
                    ObjectKinds = objectKinds,
                },
            });

        // The batch ran (tool call succeeded), but the item failed.
        Assert.False(result.IsError, Text(result));
        var operation = Structured(result).GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("failed", operation.GetProperty("status").GetString());
        Assert.Equal("protocol_error", operation.GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public async Task ListNetworkObjects_LargeListScenarioReturnsCursorWithTwentyItems()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath("list-network-objects-large");
        using var client = CreateClient();

        var result = await NetworkReadTools.NetworkRead(
            client,
            new[]
            {
                new NetworkOperationRequest
                {
                    OperationId = "large-1",
                    Operation = "list_network_objects",
                    ProjectPath = "list-network-objects-large",
                    ObjectKinds = new[] { "node" },
                    PageSize = 20,
                },
            });

        Assert.False(result.IsError, Text(result));
        var operation = Structured(result).GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("succeeded", operation.GetProperty("status").GetString());

        var resultElement = operation.GetProperty("result");
        Assert.Equal(20, resultElement.GetProperty("items").GetArrayLength());
        Assert.Equal(100, resultElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("large-list-page-2", resultElement.GetProperty("nextCursor").GetString());
    }

    private static JsonElement Structured(CallToolResult result)
        => Assert.IsType<JsonElement>(result.StructuredContent);

    private static string Text(CallToolResult result)
        => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

}
