using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// End-to-end evidence that the dedicated network tools use a single hardware snapshot for
/// each preview/apply attempt, bind tokens to the exact ordered request, and — for the migrated
/// <c>network_read</c> — return each worker payload as declared JSON rather than a nested string.
/// </summary>
public class NetworkOperationFakeWorkerTests
{
    private const string Scenario = "network-roundtrip";

    private static OpennessWorkerClient CreateClient()
        => new(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

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
        DeviceName = "PLC_1",
    };

    private static NetworkOperationRequest ConfigureDevice(string operationId, string ipAddress = "192.168.0.10") => new()
    {
        OperationId = operationId,
        Operation = "configure_network_device",
        ProjectPath = Scenario,
        Target = new NetworkObjectTarget { DeviceName = "PLC_1", NodeId = "node-1" },
        Changes = new NetworkDeviceChanges { IpAddress = ipAddress },
    };

    [Fact]
    public async Task NetworkRead_HardwareAndCatalogSucceedInRequestedOrderWithDeclaredJsonResults()
    {
        using var client = CreateClient();

        var result = await NetworkReadTools.NetworkRead(
            client,
            new[] { ReadHardware("hardware"), SearchCatalog("catalog") });

        Assert.False(result.IsError);
        var root = Assert.IsType<JsonElement>(result.StructuredContent);
        var operations = root.GetProperty("batch").GetProperty("operations");

        Assert.True(root.GetProperty("success").GetBoolean());
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
        using var client = CreateClient();

        var result = await NetworkReadTools.NetworkRead(client, new[] { ReadHardware("hardware") });

        Assert.False(result.IsError);
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
    public async Task NetworkWrite_UsesOneSnapshotPerAttemptAndEnforcesTokenLifecycle()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        using var client = CreateClient();
        var operations = new[] { AddDevice("add"), ConfigureDevice("configure") };

        var preview = await NetworkWriteTools.NetworkWrite(client, safety, operations);
        var token = SafetyToken(preview);

        var applied = await NetworkWriteTools.NetworkWrite(client, safety, operations, confirm: true, safetyToken: token);
        var results = Structured(applied).GetProperty("batch").GetProperty("operations");
        Assert.True(Structured(applied).GetProperty("success").GetBoolean());
        Assert.Equal("add", results[0].GetProperty("operationId").GetString());
        Assert.Equal("succeeded", results[0].GetProperty("status").GetString());

        // Each result is the declared contract type as JSON, never a nested JSON string. The
        // scenario stamps its request sequence into the contract's own free-text members, so the
        // apply is still provably the third and fourth worker requests of this round trip.
        Assert.Equal(JsonValueKind.Object, results[0].GetProperty("result").ValueKind);
        Assert.Equal("seq:3", results[0].GetProperty("result").GetProperty("warnings")[0].GetString());
        Assert.Equal("configure", results[1].GetProperty("operationId").GetString());
        Assert.Equal("succeeded", results[1].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Object, results[1].GetProperty("result").ValueKind);
        Assert.Equal("seq:4", results[1].GetProperty("result").GetProperty("messages")[0].GetString());

        var replay = await NetworkWriteTools.NetworkWrite(client, safety, operations, confirm: true, safetyToken: token);
        Assert.True(replay.IsError);
        Assert.Contains("Safety token", Text(replay));

        var secondToken = SafetyToken(await NetworkWriteTools.NetworkWrite(client, safety, operations));

        var changedInput = new[] { AddDevice("add"), ConfigureDevice("configure", ipAddress: "192.168.0.11") };
        var changedApply = await NetworkWriteTools.NetworkWrite(
            client,
            safety,
            changedInput,
            confirm: true,
            safetyToken: secondToken);

        Assert.True(changedApply.IsError);
        Assert.Contains("input does not match", Text(changedApply));
    }

    /// <summary>
    /// The multi-homed device (Task 7) has two REAL, existing nodes - node-plc and node-db - not a
    /// missing one and not an ambiguous one. A preview resolved against node-plc binds target
    /// evidence (device item path, interface, node name/id) specific to that node; retargeting the
    /// SAME operationId at apply time to the other, equally real node must still be rejected as a
    /// different target, since the safety token binds exactly which node was matched, not merely
    /// that some node exists.
    /// </summary>
    [Fact]
    public async Task NetworkWrite_ChangedNodeIdBetweenPreviewAndApplyIsRejectedAsADifferentTarget()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        using var client = CreateClient();
        const string scenario = "multi-homed-network";

        static NetworkOperationRequest[] Operations(string scenario, string nodeId) => new[]
        {
            new NetworkOperationRequest
            {
                OperationId = "configure",
                Operation = "configure_network_device",
                ProjectPath = scenario,
                Target = new NetworkObjectTarget { DeviceName = "PC_1", NodeId = nodeId },
                Changes = new NetworkDeviceChanges { IpAddress = "192.168.0.55" },
            },
        };

        var preview = await NetworkWriteTools.NetworkWrite(client, safety, Operations(scenario, "node-plc"));
        var token = SafetyToken(preview);

        var applied = await NetworkWriteTools.NetworkWrite(
            client, safety, Operations(scenario, "node-db"), confirm: true, safetyToken: token);

        Assert.True(applied.IsError);
        Assert.Contains("different target", Text(applied));
    }

    /// <summary>
    /// Two nodes on the SAME device reporting the SAME nodeId: NetworkIdentityResolver's
    /// ambiguous-match rule must fail the preview closed (postcondition_failed) and issue no token,
    /// proven here through the actual NetworkWriteTools/FakeWorker wiring rather than only the pure
    /// resolver unit tests.
    /// </summary>
    [Fact]
    public async Task NetworkWrite_AmbiguousNodeIdFailsClosedWithNoTokenIssued()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        using var client = CreateClient();

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
        Assert.Equal(0, safety.ActiveTokenCount);
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
        var safety = audit.CreateSafety();
        using var client = CreateClient();

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
        Assert.Equal(0, safety.ActiveTokenCount);
    }

    private static JsonElement Structured(CallToolResult result)
        => Assert.IsType<JsonElement>(result.StructuredContent);

    private static string Text(CallToolResult result)
        => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static string SafetyToken(CallToolResult preview)
    {
        var token = Structured(preview).GetProperty("preview").GetProperty("safetyToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        return token!;
    }
}
