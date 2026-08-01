using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// End-to-end evidence that the dedicated network tools use a single hardware snapshot for
/// each preview/apply attempt, bind tokens to the exact ordered request, and retain worker JSON
/// payloads as string-valued operation results.
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
        DeviceName = "PLC_1",
        IpAddress = ipAddress,
    };

    [Fact]
    public async Task NetworkRead_HardwareAndCatalogSucceedInRequestedOrderWithStringResults()
    {
        using var client = CreateClient();

        var result = await NetworkReadTools.NetworkRead(
            client,
            new[] { ReadHardware("hardware"), SearchCatalog("catalog") });

        using var document = JsonDocument.Parse(result);
        var operations = document.RootElement.GetProperty("operations");

        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("hardware", operations[0].GetProperty("operationId").GetString());
        Assert.Equal("succeeded", operations[0].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, operations[0].GetProperty("result").ValueKind);
        Assert.Equal("hardware", JsonDocument.Parse(operations[0].GetProperty("result").GetString()!)
            .RootElement.GetProperty("kind").GetString());
        Assert.Equal("catalog", operations[1].GetProperty("operationId").GetString());
        Assert.Equal("succeeded", operations[1].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, operations[1].GetProperty("result").ValueKind);
        Assert.Equal("OrderNumber:TEST", JsonDocument.Parse(operations[1].GetProperty("result").GetString()!)
            .RootElement[0].GetProperty("typeIdentifier").GetString());
    }

    [Fact]
    public async Task NetworkWrite_UsesOneSnapshotPerAttemptAndEnforcesTokenLifecycle()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        using var client = CreateClient();
        var operations = new[] { AddDevice("add"), ConfigureDevice("configure") };

        var preview = await NetworkWriteTools.NetworkWrite(client, safety, operations);
        using var previewDocument = JsonDocument.Parse(preview);
        var token = previewDocument.RootElement.GetProperty("safetyToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var applied = await NetworkWriteTools.NetworkWrite(client, safety, operations, confirm: true, safetyToken: token);
        using (var appliedDocument = JsonDocument.Parse(applied))
        {
            var results = appliedDocument.RootElement.GetProperty("operations");
            Assert.True(appliedDocument.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("add", results[0].GetProperty("operationId").GetString());
            Assert.Equal("succeeded", results[0].GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.String, results[0].GetProperty("result").ValueKind);
            Assert.Contains("\"seq\":3", results[0].GetProperty("result").GetString());
            Assert.Equal("configure", results[1].GetProperty("operationId").GetString());
            Assert.Equal("succeeded", results[1].GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.String, results[1].GetProperty("result").ValueKind);
            Assert.Contains("\"seq\":4", results[1].GetProperty("result").GetString());
        }

        var replay = await NetworkWriteTools.NetworkWrite(client, safety, operations, confirm: true, safetyToken: token);
        Assert.Contains("Safety token", replay);

        var secondPreview = await NetworkWriteTools.NetworkWrite(client, safety, operations);
        using var secondPreviewDocument = JsonDocument.Parse(secondPreview);
        var secondToken = secondPreviewDocument.RootElement.GetProperty("safetyToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(secondToken));

        var changedInput = new[] { AddDevice("add"), ConfigureDevice("configure", ipAddress: "192.168.0.11") };
        var changedApply = await NetworkWriteTools.NetworkWrite(
            client,
            safety,
            changedInput,
            confirm: true,
            safetyToken: secondToken);

        Assert.Contains("input does not match", changedApply);
    }
}
