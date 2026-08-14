using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// End-to-end evidence for the structured I/O map through the real <c>network_read</c> tool and
/// the FakeWorker: ioDetails appear only when requested, the text block and structuredContent are
/// one canonical document, a malformed ioDetails payload becomes <c>protocol_error</c>, the read
/// stays permitted in read-only mode, and the internal network-write snapshot stays lightweight.
/// </summary>
public class NetworkIoMapFakeWorkerTests
{
    private const string Scenario = "network-io-map";

    private static OpennessWorkerClient CreateClient(McpAccessMode mode = McpAccessMode.ReadWrite)
        => new(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(mode));

    private static NetworkOperationRequest ReadHardware(
        string id,
        string scenario,
        bool? includeIoDetails = null,
        bool? includeTagMatches = null,
        string? deviceName = null,
        string? plcName = null) => new()
    {
        OperationId = id,
        Operation = "read_hardware_config",
        ProjectPath = scenario,
        DeviceName = deviceName,
        PlcName = plcName,
        IncludeIoDetails = includeIoDetails,
        IncludeTagMatches = includeTagMatches,
    };

    [Fact]
    public async Task NetworkRead_IncludeIoDetailsReturnsStructuredAddressesChannelsAndTagMatches()
    {
        using var client = CreateClient();

        var result = await NetworkReadTools.NetworkRead(
            client,
            new[]
            {
                ReadHardware("io-map", Scenario, includeIoDetails: true, includeTagMatches: true),
            });

        Assert.False(result.IsError);
        var operation = AssertOneCanonicalDocument(result)
            .GetProperty("batch")
            .GetProperty("operations")[0];
        Assert.Equal("succeeded", operation.GetProperty("status").GetString());

        var resultElement = operation.GetProperty("result");
        Assert.Equal(JsonValueKind.Object, resultElement.ValueKind);
        var deviceItem = resultElement.GetProperty("devices")[0].GetProperty("items")[0];
        Assert.Equal("DI_16", deviceItem.GetProperty("name").GetString());

        var ioDetails = deviceItem.GetProperty("ioDetails");
        Assert.Equal(JsonValueKind.Object, ioDetails.ValueKind);

        var addresses = ioDetails.GetProperty("addresses");
        Assert.Equal(2, addresses.GetArrayLength());
        Assert.Equal("Input", addresses[0].GetProperty("ioType").GetString());
        Assert.Equal(4, addresses[0].GetProperty("startAddress").GetInt32());
        Assert.Equal(2, addresses[0].GetProperty("length").GetInt32());
        Assert.Equal("Device", addresses[0].GetProperty("context").GetString());
        Assert.Equal(new[] { "PLC_1" }, addresses[0].GetProperty("controllerNames").EnumerateArray().Select(e => e.GetString()));

        var channels = ioDetails.GetProperty("channels");
        Assert.Equal(2, channels.GetArrayLength());

        var digital = channels[0];
        Assert.Equal(0, digital.GetProperty("number").GetInt32());
        Assert.Equal("Input", digital.GetProperty("ioType").GetString());
        Assert.Equal("Digital", digital.GetProperty("type").GetString());
        Assert.Equal(32, digital.GetProperty("channelAddressBits").GetInt32());
        Assert.Equal(1u, digital.GetProperty("channelWidthBits").GetUInt32());
        Assert.Equal("%I4.0", digital.GetProperty("logicalAddress").GetString());

        // Two tags match the same DI channel — multiple tags per channel is supported.
        var digitalMatches = digital.GetProperty("tagMatches").EnumerateArray().ToList();
        Assert.Equal(2, digitalMatches.Count);
        Assert.Equal(
            new[] { "RunPermit", "StartButton" },
            digitalMatches.Select(match => match.GetProperty("name").GetString()).ToArray());

        var analog = channels[1];
        Assert.Equal("Analog", analog.GetProperty("type").GetString());
        Assert.Equal("%IW64", analog.GetProperty("logicalAddress").GetString());
        var analogMatch = Assert.Single(analog.GetProperty("tagMatches").EnumerateArray());
        Assert.Equal("AnalogIn", analogMatch.GetProperty("name").GetString());
        Assert.Equal("Tag table_1", analogMatch.GetProperty("tableName").GetString());
        Assert.Equal("/", analogMatch.GetProperty("folderPath").GetString());
    }

    [Fact]
    public async Task NetworkRead_WithoutIncludeIoDetailsOmitsIoDetailsEntirely()
    {
        using var client = CreateClient();

        var result = await NetworkReadTools.NetworkRead(
            client,
            new[] { ReadHardware("plain", Scenario) });

        Assert.False(result.IsError);
        var canonicalText = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        var operation = AssertOneCanonicalDocument(result)
            .GetProperty("batch")
            .GetProperty("operations")[0];

        Assert.Equal("succeeded", operation.GetProperty("status").GetString());
        Assert.DoesNotContain("ioDetails", operation.GetProperty("result").GetRawText());
        Assert.DoesNotContain("ioDetails", canonicalText);
    }

    [Fact]
    public async Task NetworkRead_DeviceNameFilterIsForwardedToTheWorker()
    {
        using var client = CreateClient();

        var result = await NetworkReadTools.NetworkRead(
            client,
            new[]
            {
                ReadHardware("filtered", Scenario, includeIoDetails: true, deviceName: "PLC_1", plcName: "PLC_1"),
            });

        Assert.False(result.IsError);
        var operation = AssertOneCanonicalDocument(result)
            .GetProperty("batch")
            .GetProperty("operations")[0];
        Assert.Equal("succeeded", operation.GetProperty("status").GetString());
        Assert.Equal("PLC_1", operation.GetProperty("result").GetProperty("devices")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task NetworkRead_MalformedIoDetailsPayloadBecomesProtocolErrorWithoutEchoingIt()
    {
        using var client = CreateClient();

        var result = await NetworkReadTools.NetworkRead(
            client,
            new[] { ReadHardware("malformed", "network-io-map-malformed", includeIoDetails: true) });

        // The batch ran (tool call is not an error), but the item failed the declared contract.
        Assert.False(result.IsError);
        var operation = AssertOneCanonicalDocument(result)
            .GetProperty("batch")
            .GetProperty("operations")[0];
        Assert.Equal("failed", operation.GetProperty("status").GetString());
        Assert.Equal("protocol_error", operation.GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, operation.GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task NetworkRead_ReadOnlyModePermitsTheIoMapRead()
    {
        using var client = CreateClient(McpAccessMode.ReadOnly);

        var result = await NetworkReadTools.NetworkRead(
            client,
            new[]
            {
                ReadHardware("readonly", Scenario, includeIoDetails: true, includeTagMatches: true),
            });

        Assert.False(result.IsError);
        var operation = AssertOneCanonicalDocument(result)
            .GetProperty("batch")
            .GetProperty("operations")[0];
        Assert.Equal("succeeded", operation.GetProperty("status").GetString());
    }

    [Fact]
    public async Task InternalSnapshotRead_ThroughTheIoMapScenarioStaysLightweight()
    {
        // NetworkSafetySnapshot.ReadCurrentStateAsync uses the default read (no ioDetails). Even
        // against the io-map scenario, the snapshot state must not contain ioDetails, so the
        // safety-token hash stays byte-identical to the legacy hardware shape.
        using var client = CreateClient();

        var snapshot = await NetworkSafetySnapshot.ReadCurrentStateAsync(client, Scenario);

        Assert.True(snapshot.Success, snapshot.Error);
        var canonical = CanonicalJson.Serialize(snapshot.State!);
        Assert.DoesNotContain("ioDetails", canonical);
    }

    private static JsonElement AssertOneCanonicalDocument(CallToolResult result)
    {
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal(CanonicalJson.Serialize(structured), text);
        using var textDocument = JsonDocument.Parse(text);
        Assert.True(JsonElement.DeepEquals(structured, textDocument.RootElement));
        return structured;
    }
}
