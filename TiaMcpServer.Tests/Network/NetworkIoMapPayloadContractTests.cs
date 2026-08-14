using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Contract tests for the I/O-map extensions of <c>read_hardware_config</c>: a payload carrying
/// <c>ioDetails</c> is accepted only when every declared non-null collection and nested object is
/// present; anything else becomes <c>protocol_error</c> without echoing the rejected payload.
/// </summary>
public class NetworkIoMapPayloadContractTests
{
    private const string LeakToken = "io-map-leak-canary";

    private static StructuredOperationItem Project(string payload)
        => NetworkPayloadContract.Project(
            new NetworkOperationRequest { OperationId = "op-1", Operation = "read_hardware_config" },
            WorkerCallResult.Ok(payload));

    private static StructuredOperationItem ProjectWithDiagnostic(string payload, List<string> diagnostics)
        => NetworkPayloadContract.Project(
            new NetworkOperationRequest { OperationId = "op-1", Operation = "read_hardware_config" },
            WorkerCallResult.Ok(payload),
            diagnostics.Add);

    private const string ValidIoDetailsPayload = """
        {
          "devices": [
            {
              "name": "PLC_1",
              "typeIdentifier": "OrderNumber:TEST",
              "items": [
                {
                  "name": "DI_16",
                  "typeIdentifier": "OrderNumber:TEST",
                  "positionNumber": 1,
                  "address": null,
                  "selectable": false,
                  "selector": null,
                  "selectorDiagnostics": ["No selector fixture for this item."],
                  "communicationConnections": [],
                  "networkInterfaces": [],
                  "items": [],
                  "ioDetails": {
                    "addresses": [
                      {
                        "ioType": "Input",
                        "startAddress": 4,
                        "length": 2,
                        "context": "Device",
                        "controllerNames": ["PLC_1"]
                      }
                    ],
                    "channels": [
                      {
                        "number": 0,
                        "ioType": "Input",
                        "type": "Digital",
                        "channelAddressBits": 32,
                        "channelWidthBits": 1,
                        "logicalAddress": "%I4.0",
                        "tagMatches": [
                          {
                            "name": "StartButton",
                            "dataType": "Bool",
                            "logicalAddress": "%I4.0",
                            "tableName": "Tag table_1",
                            "folderPath": "/"
                          }
                        ]
                      }
                    ]
                  }
                }
              ]
            }
          ],
          "subnets": [],
          "messages": []
        }
        """;

    [Fact]
    public void Project_AcceptsAValidIoDetailsPayload()
    {
        var item = Project(ValidIoDetailsPayload);

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.Null(item.Failure);

        var ioDetails = item.Result!.Value
            .GetProperty("devices")[0]
            .GetProperty("items")[0]
            .GetProperty("ioDetails");
        Assert.Equal(JsonValueKind.Object, ioDetails.ValueKind);
        Assert.Single(ioDetails.GetProperty("addresses").EnumerateArray());
        Assert.Single(ioDetails.GetProperty("channels").EnumerateArray());
        Assert.Equal("%I4.0", ioDetails.GetProperty("channels")[0].GetProperty("logicalAddress").GetString());
        Assert.Single(ioDetails.GetProperty("channels")[0].GetProperty("tagMatches").EnumerateArray());
    }

    [Fact]
    public void Project_AcceptsIoDetailsWithEmptyCollectionsAndNullScalars()
    {
        // Unreadable scalars stay null and empty collections are legal: only EXPLICIT null
        // collections (which CLR initialization can never produce) are rejected.
        var payload = """
            {
              "devices": [
                {
                  "name": "PLC_1",
                  "items": [
                    {
                      "networkInterfaces": [],
                      "communicationConnections": [],
                      "items": [],
                      "selectable": false,
                      "selector": null,
                      "selectorDiagnostics": ["unavailable"],
                      "ioDetails": {
                        "addresses": [
                          {"ioType": null, "startAddress": null, "length": null, "context": null, "controllerNames": []}
                        ],
                        "channels": [
                          {"number": null, "ioType": null, "type": null, "channelAddressBits": null, "channelWidthBits": null, "logicalAddress": null, "tagMatches": []}
                        ]
                      }
                    }
                  ]
                }
              ],
              "subnets": [],
              "messages": []
            }
            """;

        var item = Project(payload);

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.Null(item.Failure);
    }

    [Fact]
    public void Project_AcceptsADiagnosisAddressWithNullStartAndLength()
    {
        // V21 reports a negative start address (and length) for Diagnosis-type addresses; the
        // worker normalizes those to null, which is the normalized shape the host accepts. Only an
        // actual negative VALUE is still rejected below.
        var payload = """
            {
              "devices": [
                {
                  "name": "PLC_1",
                  "items": [
                    {
                      "networkInterfaces": [],
                      "communicationConnections": [],
                      "items": [],
                      "selectable": false,
                      "selector": null,
                      "selectorDiagnostics": ["unavailable"],
                      "ioDetails": {
                        "addresses": [
                          {"ioType": "Diagnosis", "startAddress": null, "length": null, "context": null, "controllerNames": []}
                        ],
                        "channels": []
                      }
                    }
                  ]
                }
              ],
              "subnets": [],
              "messages": []
            }
            """;

        var item = Project(payload);

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.Null(item.Failure);
        var address = item.Result!.Value
            .GetProperty("devices")[0]
            .GetProperty("items")[0]
            .GetProperty("ioDetails")
            .GetProperty("addresses")[0];
        Assert.Equal("Diagnosis", address.GetProperty("ioType").GetString());
        Assert.Equal(JsonValueKind.Null, address.GetProperty("startAddress").ValueKind);
        Assert.Equal(JsonValueKind.Null, address.GetProperty("length").ValueKind);
    }

    [Fact]
    public void Project_RejectsExplicitNullIoDetailsAddressesCollection()
    {
        var payload = """
            {
              "devices": [
                {
                  "name": "PLC_1",
                  "items": [
                    {
                      "networkInterfaces": [],
                      "communicationConnections": [],
                      "items": [],
                      "selectable": false,
                      "selector": null,
                      "selectorDiagnostics": ["unavailable"],
                      "ioDetails": {
                        "addresses": null,
                        "channels": []
                      }
                    }
                  ]
                }
              ],
              "subnets": [],
              "messages": ["IO-MAP-LEAK-CANARY"]
            }
            """.Replace("IO-MAP-LEAK-CANARY", LeakToken);

        var item = Project(payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Null(item.Result);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain(LeakToken, item.Failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(LeakToken, CanonicalJson.Serialize(item), StringComparison.Ordinal);
    }

    [Fact]
    public void Project_RejectsExplicitNullIoDetailsChannelsCollection()
    {
        var payload = """
            {
              "devices": [
                {
                  "name": "PLC_1",
                  "items": [
                    {
                      "networkInterfaces": [],
                      "communicationConnections": [],
                      "items": [],
                      "selectable": false,
                      "selector": null,
                      "selectorDiagnostics": ["unavailable"],
                      "ioDetails": {
                        "addresses": [],
                        "channels": null
                      }
                    }
                  ]
                }
              ],
              "subnets": [],
              "messages": ["IO-MAP-LEAK-CANARY"]
            }
            """.Replace("IO-MAP-LEAK-CANARY", LeakToken);

        var item = Project(payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain(LeakToken, item.Failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(LeakToken, CanonicalJson.Serialize(item), StringComparison.Ordinal);
    }

    [Fact]
    public void Project_RejectsExplicitNullControllerNamesOnAnAddress()
    {
        var payload = """
            {
              "devices": [
                {
                  "name": "PLC_1",
                  "items": [
                    {
                      "networkInterfaces": [],
                      "communicationConnections": [],
                      "items": [],
                      "selectable": false,
                      "selector": null,
                      "selectorDiagnostics": ["unavailable"],
                      "ioDetails": {
                        "addresses": [{"ioType": "Input", "controllerNames": null}],
                        "channels": []
                      }
                    }
                  ]
                }
              ],
              "subnets": [],
              "messages": ["IO-MAP-LEAK-CANARY"]
            }
            """.Replace("IO-MAP-LEAK-CANARY", LeakToken);

        var item = Project(payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    [Fact]
    public void Project_RejectsExplicitNullTagMatchesOnAChannel()
    {
        var payload = """
            {
              "devices": [
                {
                  "name": "PLC_1",
                  "items": [
                    {
                      "networkInterfaces": [],
                      "communicationConnections": [],
                      "items": [],
                      "selectable": false,
                      "selector": null,
                      "selectorDiagnostics": ["unavailable"],
                      "ioDetails": {
                        "addresses": [],
                        "channels": [{"number": 0, "ioType": "Input", "tagMatches": null}]
                      }
                    }
                  ]
                }
              ],
              "subnets": [],
              "messages": ["IO-MAP-LEAK-CANARY"]
            }
            """.Replace("IO-MAP-LEAK-CANARY", LeakToken);

        var item = Project(payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain(LeakToken, item.Failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(LeakToken, CanonicalJson.Serialize(item), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"name":null,"dataType":"Bool","logicalAddress":"%I4.0","tableName":"T","folderPath":"/"}""")]
    [InlineData("""{"name":"StartButton","dataType":null,"logicalAddress":"%I4.0","tableName":"T","folderPath":"/"}""")]
    [InlineData("""{"name":"StartButton","dataType":"Bool","logicalAddress":null,"tableName":"T","folderPath":"/"}""")]
    [InlineData("""{"name":"StartButton","dataType":"Bool","logicalAddress":"%I4.0","tableName":null,"folderPath":"/"}""")]
    [InlineData("""{"name":"StartButton","dataType":"Bool","logicalAddress":"%I4.0","tableName":"T","folderPath":null}""")]
    public void Project_RejectsNullDeclaredStringMemberInsideATagMatch(string tagMatchJson)
    {
        var payload = $$"""
            {
              "devices": [
                {
                  "name": "PLC_1",
                  "items": [
                    {
                      "networkInterfaces": [],
                      "communicationConnections": [],
                      "items": [],
                      "selectable": false,
                      "selector": null,
                      "selectorDiagnostics": ["unavailable"],
                      "ioDetails": {
                        "addresses": [],
                        "channels": [{"number": 0, "ioType": "Input", "tagMatches": [{{tagMatchJson}}]}]
                      }
                    }
                  ]
                }
              ],
              "subnets": [],
              "messages": ["IO-MAP-LEAK-CANARY"]
            }
            """.Replace("IO-MAP-LEAK-CANARY", LeakToken);

        var diagnostics = new List<string>();
        var item = ProjectWithDiagnostic(payload, diagnostics);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain(LeakToken, item.Failure.Message, StringComparison.Ordinal);

        // The precise rejection location is reported through the bounded protocol diagnostic
        // (validator chain), never through the public failure message.
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("ValidateIoDetails", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain(LeakToken, diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_RejectsNegativeStartAddressAndChannelAddressBits()
    {
        var negativeStart = ValidIoDetailsPayload.Replace("\"startAddress\": 4", "\"startAddress\": -4");
        var negativeBits = ValidIoDetailsPayload.Replace("\"channelAddressBits\": 32", "\"channelAddressBits\": -1");

        var startItem = Project(negativeStart);
        var bitsItem = Project(negativeBits);

        Assert.Equal(OperationBatchStatus.Failed, startItem.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, startItem.Failure!.Category);
        Assert.Equal(OperationBatchStatus.Failed, bitsItem.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, bitsItem.Failure!.Category);
    }

    [Fact]
    public void Project_RejectsNullNestedAddressInsideIoDetails()
    {
        var payload = """
            {
              "devices": [
                {
                  "name": "PLC_1",
                  "items": [
                    {
                      "networkInterfaces": [],
                      "communicationConnections": [],
                      "items": [],
                      "selectable": false,
                      "selector": null,
                      "selectorDiagnostics": ["unavailable"],
                      "ioDetails": {
                        "addresses": [null],
                        "channels": []
                      }
                    }
                  ]
                }
              ],
              "subnets": [],
              "messages": []
            }
            """;

        var item = Project(payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    [Fact]
    public void Project_RejectsNullNestedChannelInsideIoDetails()
    {
        var payload = """
            {
              "devices": [
                {
                  "name": "PLC_1",
                  "items": [
                    {
                      "networkInterfaces": [],
                      "communicationConnections": [],
                      "items": [],
                      "selectable": false,
                      "selector": null,
                      "selectorDiagnostics": ["unavailable"],
                      "ioDetails": {
                        "addresses": [],
                        "channels": [null]
                      }
                    }
                  ]
                }
              ],
              "subnets": [],
              "messages": []
            }
            """;

        var item = Project(payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    [Fact]
    public void Project_LogsBoundedValidatorLocationWithoutLeakingTheRejectedPayload()
    {
        var diagnostics = new List<string>();
        var payload = """
            {
              "devices": [
                {
                  "name": "PLC_1",
                  "items": [
                    {
                      "networkInterfaces": [],
                      "communicationConnections": [],
                      "items": [],
                      "selectable": false,
                      "selector": null,
                      "selectorDiagnostics": ["unavailable"],
                      "ioDetails": {"addresses": null, "channels": []}
                    }
                  ]
                }
              ],
              "subnets": [],
              "messages": ["IO-MAP-LEAK-CANARY"]
            }
            """.Replace("IO-MAP-LEAK-CANARY", LeakToken);

        var item = ProjectWithDiagnostic(payload, diagnostics);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("ValidateIoDetails", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain(LeakToken, diagnostic, StringComparison.Ordinal);
        Assert.InRange(diagnostic.Length, 1, 512);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure?.Category);
    }

    [Fact]
    public void DecodeHardwareConfig_AcceptsAnIoDetailsPayloadForSnapshotConsumption()
    {
        // NetworkSafetySnapshot decodes through this same registry; an ioDetails payload is a
        // valid HardwareConfigInfo and must decode without throwing.
        var decoded = NetworkPayloadContract.DecodeHardwareConfig(ValidIoDetailsPayload);

        Assert.NotNull(decoded.Devices);
        Assert.NotNull(decoded.Devices[0].Items[0].IoDetails);
        Assert.Single(decoded.Devices[0].Items[0].IoDetails!.Channels);
    }

    [Fact]
    public void DecodeHardwareConfig_OfALegacyPayloadKeepsCanonicalSerializationFreeOfIoDetails()
    {
        const string legacy = """{"devices":[{"name":"PLC_1","items":[{"networkInterfaces":[],"communicationConnections":[],"items":[],"selectable":false,"selector":null,"selectorDiagnostics":["unavailable"]}]}],"subnets":[],"messages":[]}""";

        var decoded = NetworkPayloadContract.DecodeHardwareConfig(legacy);

        var canonical = CanonicalJson.Serialize(decoded);

        Assert.DoesNotContain("ioDetails", canonical);
    }
}
