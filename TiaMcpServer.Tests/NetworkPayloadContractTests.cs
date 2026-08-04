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

    [Theory]
    [InlineData(
        "list_network_objects",
        """{"items":[],"totalCount":0,"nextCursor":null,"messages":[]}""",
        JsonValueKind.Object)]
    [InlineData(
        "inspect_network_object",
        """{"kind":"node","displayName":"X1","evidence":null,"attributes":[],"messages":[]}""",
        JsonValueKind.Object)]
    public void Project_DecodesPhase3OperationsIntoTheirResultTypes(
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

    [Fact]
    public void Project_DecodesListNetworkObjectsWithAllSixKinds()
    {
        var payload = """
            {
              "items": [
                {"kind":"deviceItem","displayName":"if_1","selector":{"kind":"deviceItem","deviceName":"PLC_1","itemPath":[{"positionNumber":1}]}},
                {"kind":"networkInterface","displayName":"PROFINET interface_1","selector":{"kind":"networkInterface","deviceName":"PLC_1","interfaceName":"PROFINET interface_1"}},
                {"kind":"node","displayName":"X1","selector":{"kind":"node","deviceName":"PLC_1","nodeId":"node-1"}},
                {"kind":"subnet","displayName":"PN/IE_1","selector":{"kind":"subnet","subnetId":"subnet-1"}},
                {"kind":"ioSystem","displayName":"IO system_1","selector":{"kind":"ioSystem","subnetId":"subnet-1","number":100}},
                {"kind":"communicationConnection","displayName":"S7 connection","selector":null}
              ],
              "totalCount": 6,
              "nextCursor": null,
              "messages": []
            }
            """;

        var item = Project("list_network_objects", payload);

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.NotNull(item.Result);
        var items = item.Result!.Value.GetProperty("items");
        Assert.Equal(6, items.GetArrayLength());
        Assert.Equal("deviceItem", items[0].GetProperty("kind").GetString());
        Assert.Equal("communicationConnection", items[5].GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, items[5].GetProperty("selector").ValueKind);
    }

    [Fact]
    public void Project_DecodesInspectNetworkObjectWithAllAttributeKinds()
    {
        var payload = """
            {
              "kind": "node",
              "displayName": "X1",
              "evidence": {"kind":"node","selector":{"kind":"node","deviceName":"PLC_1","nodeId":"node-1"},"messages":[]},
              "attributes": [
                {"name":"nullAttribute","value":null},
                {"name":"stringAttribute","value":"192.168.0.10"},
                {"name":"booleanAttribute","value":"True"},
                {"name":"integerAttribute","value":"1500"},
                {"name":"numberAttribute","value":"3.14"},
                {"name":"enumAttribute","value":"Ethernet"},
                {"name":"unknownAttribute","value":null},
                {"name":"readFailed","value":null},
                {"name":"unrepresentable","value":null}
              ],
              "messages": []
            }
            """;

        var item = Project("inspect_network_object", payload);

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.NotNull(item.Result);
        var attrs = item.Result!.Value.GetProperty("attributes");
        Assert.Equal(9, attrs.GetArrayLength());
        Assert.Equal("nullAttribute", attrs[0].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, attrs[0].GetProperty("value").ValueKind);
        Assert.Equal("192.168.0.10", attrs[1].GetProperty("value").GetString());
    }

    public static TheoryData<string, string> Phase3InvalidSuccessfulPayloads() => new()
    {
        // list_network_objects: null items collection.
        { "list_network_objects", $$$"""{"items":null,"messages":["{{{LeakToken}}}"]}""" },

        // list_network_objects: unknown kind in items[].
        { "list_network_objects", $$$"""{"items":[{"kind":"{{{LeakToken}}}","displayName":"x","selector":null}],"messages":[]}""" },

        // list_network_objects: negative totalCount.
        { "list_network_objects", $$$"""{"items":[],"totalCount":-1,"messages":["{{{LeakToken}}}"]}""" },

        // list_network_objects: items.Count > totalCount.
        { "list_network_objects", $$$"""{"items":[{"kind":"node","displayName":"X1","selector":null}],"totalCount":0,"messages":["{{{LeakToken}}}"]}""" },

        // list_network_objects: selector.kind disagrees with summary kind.
        { "list_network_objects", $$$"""{"items":[{"kind":"node","displayName":"X1","selector":{"kind":"{{{LeakToken}}}","deviceName":"PLC","nodeId":"n1"}}],"messages":[]}""" },

        // inspect_network_object: unknown kind.
        { "inspect_network_object", $$$"""{"kind":"{{{LeakToken}}}","displayName":"X1","attributes":[],"messages":[]}""" },

        // inspect_network_object: duplicate attribute name.
        { "inspect_network_object", $$$"""{"kind":"node","displayName":"X1","attributes":[{"name":"IpAddress","value":"1.2.3.4"},{"name":"IpAddress","value":"{{{LeakToken}}}"}],"messages":[]}""" },

        // inspect_network_object: null attributes collection.
        { "inspect_network_object", $$$"""{"kind":"node","displayName":"{{{LeakToken}}}","attributes":null,"messages":[]}""" },

        // Wrong root kind for list_network_objects (array instead of object).
        { "list_network_objects", $$$"""["{{{LeakToken}}}"]""" },

        // Wrong root kind for inspect_network_object (array instead of object).
        { "inspect_network_object", $$$"""["{{{LeakToken}}}"]""" },
    };

    [Theory]
    [MemberData(nameof(Phase3InvalidSuccessfulPayloads))]
    public void Project_RejectsPhase3InvalidPayloadsWithoutLeakingThem(string operation, string payload)
    {
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Null(item.Result);
        Assert.NotNull(item.Failure);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain(LeakToken, item.Failure.Message, StringComparison.Ordinal);
        Assert.Contains(operation, item.Failure.Message, StringComparison.Ordinal);
    }

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

        // Null entry nested inside devices[].items[]: not checked by the top-level devices[]/
        // subnets[] null guard, so a resolver walking this tree must never see it as a real item.
        { "read_hardware_config", $$"""{"devices":[{"name":"{{LeakToken}}","items":[null]}],"subnets":[],"messages":[]}""" },

        // Null entry nested inside devices[].items[].networkInterfaces[].
        { "read_hardware_config", $$"""{"devices":[{"name":"{{LeakToken}}","items":[{"networkInterfaces":[null],"items":[]}]}],"subnets":[],"messages":[]}""" },

        // Null entry nested inside devices[].items[].networkInterfaces[].nodes[] — the exact shape
        // NetworkIdentityResolver walks to match a configure_network_device target.
        { "read_hardware_config", $$"""{"devices":[{"name":"{{LeakToken}}","items":[{"networkInterfaces":[{"name":"if1","nodes":[null]}],"items":[]}]}],"subnets":[],"messages":[]}""" },

        // Null entry nested inside devices[].items[].items[] (a nested device item, not a leaf).
        { "read_hardware_config", $$"""{"devices":[{"name":"{{LeakToken}}","items":[{"networkInterfaces":[],"items":[null]}]}],"subnets":[],"messages":[]}""" },

        // Null entry nested inside subnets[].ioSystems[].
        { "read_hardware_config", $$"""{"devices":[],"subnets":[{"name":"{{LeakToken}}","ioSystems":[null],"connectedNodeNames":[]}],"messages":[]}""" },
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

    /// <summary>
    /// A read_hardware_config payload that includes selector metadata (Task 3 addition) must be
    /// accepted by the contract: the new fields are mapped members, not unmapped strangers.
    /// </summary>
    [Fact]
    public void Project_AcceptsHardwarePayloadWithSelectorMetadata()
    {
        var payload = """
            {
              "devices": [
                {
                  "name": "PLC_1",
                  "typeIdentifier": "OrderNumber:CPU",
                  "items": [
                    {
                      "name": "PROFINET interface_1",
                      "typeIdentifier": "OrderNumber:IF",
                      "positionNumber": 0,
                      "address": null,
                      "selectable": true,
                      "selector": {
                        "kind": "deviceItem",
                        "deviceName": "PLC_1",
                        "itemPath": [
                          {"index": 0, "name": "PROFINET interface_1", "positionNumber": 0, "typeIdentifier": "OrderNumber:IF"}
                        ]
                      },
                      "selectorDiagnostics": [],
                      "networkInterfaces": [
                        {
                          "name": "PROFINET interface_1",
                          "selectable": true,
                          "selector": {
                            "kind": "networkInterface",
                            "deviceName": "PLC_1",
                            "interfaceName": "PROFINET interface_1"
                          },
                          "selectorDiagnostics": [],
                          "nodes": [
                            {
                              "name": "X1",
                              "nodeId": "node-1",
                              "nodeType": "Ethernet",
                              "ipAddress": "192.168.0.10",
                              "selectable": true,
                              "selector": {"kind": "node", "deviceName": "PLC_1", "nodeId": "node-1"},
                              "selectorDiagnostics": []
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
                  "selectable": true,
                  "selector": {"kind": "subnet", "subnetId": "subnet-1"},
                  "selectorDiagnostics": [],
                  "ioSystems": [
                    {
                      "name": "IO system_1",
                      "number": 100,
                      "ioControllerName": "PLC_1",
                      "selectable": true,
                      "selector": {"kind": "ioSystem", "subnetId": "subnet-1", "number": 100},
                      "selectorDiagnostics": [],
                      "connectedDeviceNames": []
                    }
                  ],
                  "connectedNodeNames": []
                }
              ],
              "messages": []
            }
            """;

        var item = Project("read_hardware_config", payload);

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.Null(item.Failure);

        var device = item.Result!.Value.GetProperty("devices")[0];
        var deviceItem = device.GetProperty("items")[0];
        Assert.True(deviceItem.GetProperty("selectable").GetBoolean());
        Assert.Equal("deviceItem", deviceItem.GetProperty("selector").GetProperty("kind").GetString());

        var subnet = item.Result.Value.GetProperty("subnets")[0];
        Assert.True(subnet.GetProperty("selectable").GetBoolean());
        Assert.Equal("subnet", subnet.GetProperty("selector").GetProperty("kind").GetString());

        var ioSystem = subnet.GetProperty("ioSystems")[0];
        Assert.True(ioSystem.GetProperty("selectable").GetBoolean());
        Assert.Equal("ioSystem", ioSystem.GetProperty("selector").GetProperty("kind").GetString());
    }

    /// <summary>
    /// An explicit null for selectorDiagnostics (a declared non-null list) must be rejected.
    /// </summary>
    [Fact]
    public void Project_RejectsExplicitNullSelectorDiagnostics_OnDeviceItem()
    {
        var payload = """
            {
              "devices": [
                {
                  "name": "PLC_1",
                  "items": [
                    {
                      "networkInterfaces": [],
                      "items": [],
                      "selectable": false,
                      "selector": null,
                      "selectorDiagnostics": null
                    }
                  ]
                }
              ],
              "subnets": [],
              "messages": []
            }
            """;

        var item = Project("read_hardware_config", payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }
}
