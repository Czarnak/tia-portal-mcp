using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Contract tests for the network operation-to-result-type registry. Every declared operation must
/// decode its valid payload into declared JSON, and must reject anything else as
/// <c>protocol_error</c> without echoing the payload that failed.
/// </summary>
public class NetworkPayloadContractTests
{
    private const string LeakToken = "payload-leak-canary";

    private static StructuredOperationItem Project(string operation, string payload)
        => NetworkPayloadContract.Project(
            new NetworkOperationRequest { OperationId = "op-1", Operation = operation },
            WorkerCallResult.Ok(payload));

    [Theory]
    [InlineData(
        "read_hardware_config",
        """{"devices":[],"subnets":[],"messages":[]}""",
        JsonValueKind.Object)]
    [InlineData(
        "search_equipment_catalog",
        """[{"typeName":"CPU 1510","typeIdentifier":"OrderNumber:6ES7 510-1DJ01-0AB0/V2.0"}]""",
        JsonValueKind.Array)]
    [InlineData(
        "add_network_device",
        """{"deviceName":"PLC_1","rootItemName":"PLC_1","typeIdentifier":"OrderNumber:X","warnings":[]}""",
        JsonValueKind.Object)]
    [InlineData(
        "configure_network_device",
        """{"deviceName":"PLC_1","appliedSettings":{},"skippedSettings":{},"messages":[]}""",
        JsonValueKind.Object)]
    public void Project_DecodesEveryDeclaredOperationIntoItsResultType(
        string operation,
        string payload,
        JsonValueKind expectedKind)
    {
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.Null(item.Failure);
        Assert.NotNull(item.Result);
        Assert.Equal(expectedKind, item.Result!.Value.ValueKind);
    }

    /// <summary>
    /// The identity members Task 6 selectors consume must survive the contract under their exact
    /// declared JSON names, in a fully populated hardware tree rather than an empty one.
    /// </summary>
    [Fact]
    public void Project_DecodesNetworkIdentitiesUnderTheirDeclaredJsonNames()
    {
        var item = Project("read_hardware_config", HardwareConfigWithIdentities);

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.Null(item.Failure);

        var node = item.Result!.Value
            .GetProperty("devices")[0]
            .GetProperty("items")[0]
            .GetProperty("networkInterfaces")[0]
            .GetProperty("nodes")[0];
        var subnet = item.Result!.Value.GetProperty("subnets")[0];

        Assert.Equal("0", node.GetProperty("nodeId").GetString());
        Assert.Equal("Ethernet", node.GetProperty("nodeType").GetString());
        Assert.Equal("subnet-1", subnet.GetProperty("subnetId").GetString());
        Assert.Equal("Ethernet", subnet.GetProperty("networkType").GetString());
        Assert.Equal(100, subnet.GetProperty("ioSystems")[0].GetProperty("number").GetInt32());
    }

    private const string HardwareConfigWithIdentities = """
        {
          "devices": [
            {
              "name": "PLC_1",
              "typeIdentifier": "OrderNumber:TEST",
              "items": [
                {
                  "name": "PROFINET interface_1",
                  "typeIdentifier": "OrderNumber:TEST",
                  "positionNumber": 1,
                  "address": null,
                  "networkInterfaces": [
                    {
                      "name": "PROFINET interface_1",
                      "nodes": [
                        {
                          "name": "X1",
                          "nodeId": "0",
                          "nodeType": "Ethernet",
                          "ipAddress": "192.168.0.10",
                          "subnetMask": "255.255.255.0",
                          "pnDeviceName": "plc-1",
                          "subnetName": "PN/IE_1",
                          "ioSystemName": "IO system_1"
                        }
                      ]
                    }
                  ],
                  "items": []
                }
              ]
            }
          ],
          "subnets": [
            {
              "name": "PN/IE_1",
              "subnetId": "subnet-1",
              "networkType": "Ethernet",
              "typeIdentifier": "Ethernet",
              "ioSystems": [
                {
                  "name": "IO system_1",
                  "number": 100,
                  "ioControllerName": "PLC_1",
                  "connectedDeviceNames": ["ET200SP_1"]
                }
              ],
              "connectedNodeNames": ["PLC_1.X1"]
            }
          ],
          "messages": []
        }
        """;

    public static TheoryData<string, string> InvalidSuccessfulPayloads() => new()
    {
        // Wrong casing on an identity member: the strict registry treats it as unmapped rather
        // than tolerating it, so a selector can never bind an identity the worker mis-spelled.
        { "read_hardware_config", $$"""{"devices":[],"subnets":[{"name":"PN/IE_1","subnetID":"{{LeakToken}}","ioSystems":[],"connectedNodeNames":[]}],"messages":[]}""" },

        // Wrong member type for an identity: a numeric node id is not the declared string.
        { "read_hardware_config", $$"""{"devices":[{"name":"{{LeakToken}}","items":[{"networkInterfaces":[{"name":"PROFINET interface_1","nodes":[{"name":"X1","nodeId":0}]}],"items":[]}]}],"subnets":[],"messages":[]}""" },

        // Wrong member type for the IO-system number: a stringified number is not an integer.
        { "read_hardware_config", $$"""{"devices":[],"subnets":[{"name":"{{LeakToken}}","ioSystems":[{"name":"IO system_1","number":"100","connectedDeviceNames":[]}],"connectedNodeNames":[]}],"messages":[]}""" },

        // Unmapped member.
        { "read_hardware_config", $$"""{"devices":[],"subnets":[],"messages":[],"x":"{{LeakToken}}"}""" },

        // Explicit null defeating a non-null collection default: representable in JSON, not
        // representable through CLR initialization, so a type-specific validator must reject it.
        { "read_hardware_config", $$"""{"devices":null,"subnets":[],"messages":["{{LeakToken}}"]}""" },

        // Wrong root kind: an array where an object is declared.
        { "read_hardware_config", $$"""["{{LeakToken}}"]""" },

        // Wrong root kind: an object where an array is declared.
        { "search_equipment_catalog", $$"""{"typeName":"{{LeakToken}}"}""" },

        // Null entry inside the declared array.
        { "search_equipment_catalog", $$"""[null,{"typeName":"{{LeakToken}}","typeIdentifier":"i"}]""" },

        // Wrong member type.
        { "search_equipment_catalog", $$"""[{"typeName":7,"typeIdentifier":"{{LeakToken}}"}]""" },

        // Explicit null for a declared non-nullable string.
        { "add_network_device", $$"""{"deviceName":null,"rootItemName":"{{LeakToken}}","typeIdentifier":"t","warnings":[]}""" },

        // Duplicate member.
        { "add_network_device", $$"""{"deviceName":"a","deviceName":"{{LeakToken}}","rootItemName":"r","typeIdentifier":"t","warnings":[]}""" },

        // Explicit null dictionary.
        { "configure_network_device", $$"""{"deviceName":"{{LeakToken}}","appliedSettings":null,"skippedSettings":{},"messages":[]}""" },

        // Not JSON at all.
        { "configure_network_device", LeakToken },

        // No declared result contract for this operation.
        { "read_something_undeclared", $$"""{"value":"{{LeakToken}}"}""" },
    };

    [Theory]
    [MemberData(nameof(InvalidSuccessfulPayloads))]
    public void Project_RejectsInvalidSuccessfulPayloadsWithoutLeakingThem(string operation, string payload)
    {
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Null(item.Result);
        Assert.NotNull(item.Failure);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain(LeakToken, item.Failure.Message, StringComparison.Ordinal);
        Assert.Contains(operation, item.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_KeepsWorkerWarningsOnRejectedPayloads()
    {
        var item = NetworkPayloadContract.Project(
            new NetworkOperationRequest { OperationId = "op-1", Operation = "read_hardware_config" },
            WorkerCallResult.Ok("""{"unexpected":true}""", new[] { "degraded read" }));

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(new[] { "degraded read" }, item.Warnings);
    }

    [Fact]
    public void Project_KeepsWorkerFailureCategoryInsteadOfReclassifyingIt()
    {
        var item = NetworkPayloadContract.Project(
            new NetworkOperationRequest { OperationId = "op-1", Operation = "read_hardware_config" },
            WorkerCallResult.Fail(WorkerFailureCategories.WorkerTimeout, "no response"));

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.WorkerTimeout, item.Failure!.Category);
        Assert.Equal("no response", item.Failure.Message);
    }
}
