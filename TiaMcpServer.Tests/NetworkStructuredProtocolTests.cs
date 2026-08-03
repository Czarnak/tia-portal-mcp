using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Protocol-level evidence for the Phase 2 <c>network_read</c> JSON contract. Everything here is
/// observed through a real MCP client so the assertions cover what an agent actually receives:
/// the advertised output schema, and one canonical JSON document delivered identically as the
/// text block and as <c>structuredContent</c>.
/// </summary>
public class NetworkStructuredProtocolTests
{
    [Fact]
    public async Task NetworkRead_AdvertisesAndReturnsSingleLayerStructuredContract()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<NetworkReadTools>();

        var tool = Assert.Single(
            await harness.Client.ListToolsAsync(),
            candidate => candidate.Name == "network_read");
        Assert.NotNull(tool.ProtocolTool.OutputSchema);

        var result = await harness.Client.CallToolAsync(
            "network_read",
            new Dictionary<string, object?>
            {
                ["operations"] = new[]
                {
                    new
                    {
                        operationId = "hardware",
                        operation = "read_hardware_config",
                        projectPath = "network-roundtrip"
                    }
                }
            });

        var structured = AssertOneCanonicalDocument(result);
        Assert.Equal(
            JsonValueKind.Object,
            structured.GetProperty("batch")
                .GetProperty("operations")[0]
                .GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task NetworkWrite_AdvertisesOutputSchemaAndKeepsEnvelopePhasesMutuallyExclusive()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartAsync<NetworkWriteTools>(audit.Path);

        var tool = Assert.Single(
            await harness.Client.ListToolsAsync(),
            candidate => candidate.Name == "network_write");
        Assert.NotNull(tool.ProtocolTool.OutputSchema);

        var error = await CallWriteAsync(harness, Array.Empty<object>());
        var errorRoot = AssertOneCanonicalDocument(error);
        Assert.True(error.IsError);
        AssertOnlyPopulated(errorRoot, "error", "error");

        var preview = await CallWriteAsync(harness, WriteOperations("network-roundtrip"));
        var previewRoot = AssertOneCanonicalDocument(preview);
        Assert.False(preview.IsError);
        AssertOnlyPopulated(previewRoot, "preview", "preview");
        var token = previewRoot.GetProperty("preview").GetProperty("safetyToken").GetString();

        var applied = await CallWriteAsync(
            harness, WriteOperations("network-roundtrip"), confirm: true, safetyToken: token);
        var appliedRoot = AssertOneCanonicalDocument(applied);
        Assert.False(applied.IsError);
        AssertOnlyPopulated(appliedRoot, "apply", "batch");
        Assert.True(appliedRoot.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task NetworkWrite_ExecutedBatchWithAFailedItemIsNotAToolError()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartAsync<NetworkWriteTools>(audit.Path);
        var operations = WriteOperations("network-write-item-failure");

        var preview = AssertOneCanonicalDocument(await CallWriteAsync(harness, operations));
        var applied = await CallWriteAsync(
            harness,
            operations,
            confirm: true,
            safetyToken: preview.GetProperty("preview").GetProperty("safetyToken").GetString());

        var root = AssertOneCanonicalDocument(applied);

        // The batch ran, so this is a successful MCP call reporting a failed item — not a tool error.
        Assert.False(applied.IsError);
        AssertOnlyPopulated(root, "apply", "batch");
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("failed", root.GetProperty("batch").GetProperty("operations")[0].GetProperty("status").GetString());
    }

    /// <summary>
    /// Complete real-protocol proof for a multi-homed device (Task 7): read both ports, select the
    /// PLC-facing one by its exact nodeId, preview and apply a change to it, then read again and
    /// compare. The database-facing port must be byte-for-byte unchanged in the canonical read
    /// model - not merely "still reports the same IP", but the entire node object, proving the
    /// configure touched nothing else about it. All four calls share one MCP session and therefore
    /// one FakeWorker process, so the second read observes the SAME process-local mutable state the
    /// apply just changed.
    /// </summary>
    [Fact]
    public async Task NetworkWrite_MultiHomedFlow_ReadSelectPreviewApplyRead_ChangesOnlySelectedPortAndLeavesTheOtherByteForByteUnchanged()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartAsync<NetworkReadTools, NetworkWriteTools>(audit.Path);
        const string scenario = "multi-homed-network";

        var before = await ReadHardwareConfigAsync(harness, scenario);
        var beforePlc = FindNodeByNodeId(before, "node-plc");
        var beforeDb = FindNodeByNodeId(before, "node-db");
        Assert.Equal("192.168.0.20", beforePlc.GetProperty("ipAddress").GetString());
        Assert.Equal("10.20.30.40", beforeDb.GetProperty("ipAddress").GetString());

        var operations = new object[]
        {
            new
            {
                operationId = "configure",
                operation = "configure_network_device",
                projectPath = scenario,
                target = new { deviceName = "PC_1", nodeId = "node-plc" },
                changes = new { ipAddress = "192.168.0.99" },
            },
        };

        var preview = AssertOneCanonicalDocument(await CallWriteAsync(harness, operations));
        var token = preview.GetProperty("preview").GetProperty("safetyToken").GetString();

        // The preview's own resolved target evidence already proves node-plc (not node-db, not a
        // guess) is what was matched, before anything is applied.
        var previewTarget = preview.GetProperty("preview").GetProperty("target")[0];
        Assert.Equal("node-plc", previewTarget.GetProperty("nodeId").GetString());
        Assert.Equal("PC_1", previewTarget.GetProperty("deviceName").GetString());

        var applied = await CallWriteAsync(harness, operations, confirm: true, safetyToken: token);
        var appliedRoot = AssertOneCanonicalDocument(applied);
        Assert.False(applied.IsError);
        Assert.True(appliedRoot.GetProperty("success").GetBoolean());
        Assert.Equal(
            "succeeded",
            appliedRoot.GetProperty("batch").GetProperty("operations")[0].GetProperty("status").GetString());

        var after = await ReadHardwareConfigAsync(harness, scenario);
        var afterPlc = FindNodeByNodeId(after, "node-plc");
        var afterDb = FindNodeByNodeId(after, "node-db");

        Assert.Equal("192.168.0.99", afterPlc.GetProperty("ipAddress").GetString());
        Assert.False(JsonElement.DeepEquals(beforePlc, afterPlc));
        Assert.True(JsonElement.DeepEquals(beforeDb, afterDb));
    }

    /// <summary>
    /// A caller that reorders JSON keys inside <c>target</c>/<c>changes</c> between preview and
    /// apply has not changed anything - every value, type, and array order is identical, only pure
    /// object-property order differs. The safety token binds through canonical (sorted-key)
    /// hashing, so it must still validate; only a genuinely different value, type, or array order
    /// may reject it.
    /// </summary>
    [Fact]
    public async Task NetworkWrite_PropertyReorderingOnlyStillValidatesAgainstTheSameToken()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartAsync<NetworkWriteTools>(audit.Path);

        var previewOperation = new Dictionary<string, object?>
        {
            ["operationId"] = "configure",
            ["operation"] = "configure_network_device",
            ["projectPath"] = "network-roundtrip",
            ["target"] = JsonSerializer.Deserialize<JsonElement>("""{"deviceName":"PLC_1","nodeId":"node-1"}"""),
            ["changes"] = JsonSerializer.Deserialize<JsonElement>(
                """{"ipAddress":"192.168.0.10","subnetMask":"255.255.255.0"}"""),
        };
        var applyOperation = new Dictionary<string, object?>
        {
            ["operationId"] = "configure",
            ["operation"] = "configure_network_device",
            ["projectPath"] = "network-roundtrip",
            ["target"] = JsonSerializer.Deserialize<JsonElement>("""{"nodeId":"node-1","deviceName":"PLC_1"}"""),
            ["changes"] = JsonSerializer.Deserialize<JsonElement>(
                """{"subnetMask":"255.255.255.0","ipAddress":"192.168.0.10"}"""),
        };

        var preview = AssertOneCanonicalDocument(
            await CallWriteAsync(harness, new object[] { previewOperation }));
        var token = preview.GetProperty("preview").GetProperty("safetyToken").GetString();

        var applied = await CallWriteAsync(
            harness, new object[] { applyOperation }, confirm: true, safetyToken: token);
        var appliedRoot = AssertOneCanonicalDocument(applied);

        Assert.False(applied.IsError);
        Assert.Equal("apply", appliedRoot.GetProperty("phase").GetString());
        Assert.True(appliedRoot.GetProperty("success").GetBoolean());
    }

    /// <summary>
    /// A worker success envelope whose payload fails its declared contract must not silently stop a
    /// read batch: reads are independent, so a later operation still runs.
    /// </summary>
    [Fact]
    public async Task NetworkRead_InvalidSuccessPayloadBecomesProtocolErrorAndLaterOperationsStillRun()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<NetworkReadTools>();

        var result = await harness.Client.CallToolAsync(
            "network_read",
            new Dictionary<string, object?>
            {
                ["operations"] = new object[]
                {
                    new
                    {
                        operationId = "bad-catalog",
                        operation = "search_equipment_catalog",
                        projectPath = "invalid-network-success-payload",
                        query = "OrderNumber:TEST",
                    },
                    new
                    {
                        operationId = "good-hardware",
                        operation = "read_hardware_config",
                        projectPath = "invalid-network-success-payload",
                    },
                },
            });

        var root = AssertOneCanonicalDocument(result);
        Assert.False(result.IsError);

        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal("failed", items[0].GetProperty("status").GetString());
        Assert.Equal(
            WorkerFailureCategories.ProtocolError,
            items[0].GetProperty("failure").GetProperty("category").GetString());
        Assert.DoesNotContain(
            "unexpectedShape", items[0].GetProperty("failure").GetProperty("message").GetString());

        // The later, independent read still ran despite the earlier protocol_error.
        Assert.Equal("succeeded", items[1].GetProperty("status").GetString());
    }

    /// <summary>
    /// The write-side counterpart: a worker success envelope with a contract-violating payload
    /// becomes <c>protocol_error</c>, stops the sequential apply (later writes are skipped), warns
    /// that a mutation may already have occurred, and stays MCP <c>isError:false</c> because a
    /// usable batch (one that ran) exists.
    /// </summary>
    [Fact]
    public async Task NetworkWrite_InvalidSuccessPayloadBecomesProtocolErrorSkipsLaterWritesWarnsAndKeepsIsErrorFalse()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartAsync<NetworkWriteTools>(audit.Path);
        var operations = new object[]
        {
            new
            {
                operationId = "bad-create",
                operation = "add_network_device",
                projectPath = "invalid-network-success-payload",
                typeIdentifier = "OrderNumber:TEST",
                deviceName = "PLC_1",
            },
            new
            {
                operationId = "never-reached",
                operation = "add_network_device",
                projectPath = "invalid-network-success-payload",
                typeIdentifier = "OrderNumber:TEST",
                deviceName = "PLC_2",
            },
        };

        var preview = AssertOneCanonicalDocument(await CallWriteAsync(harness, operations));
        var token = preview.GetProperty("preview").GetProperty("safetyToken").GetString();

        var applied = await CallWriteAsync(harness, operations, confirm: true, safetyToken: token);
        var root = AssertOneCanonicalDocument(applied);

        // The batch RAN (a usable batch exists), so this stays a successful MCP call even though
        // the batch itself reports failure.
        Assert.False(applied.IsError);
        Assert.False(root.GetProperty("success").GetBoolean());

        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal("failed", items[0].GetProperty("status").GetString());
        Assert.Equal(
            WorkerFailureCategories.ProtocolError,
            items[0].GetProperty("failure").GetProperty("category").GetString());
        Assert.DoesNotContain(
            "unexpectedShape", items[0].GetProperty("failure").GetProperty("message").GetString());

        Assert.Equal("skipped", items[1].GetProperty("status").GetString());
        Assert.Equal(
            StructuredOperationSkipReasons.EarlierOperationFailed,
            items[1].GetProperty("skipReason").GetString());

        var warning = items[0].GetProperty("warnings")[0].GetString();
        Assert.Contains("may already have changed", warning);
        Assert.Contains("no rollback", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NetworkWrite_InputSchemaDoesNotAdvertiseLegacyFlatConfigureFields()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartAsync<NetworkWriteTools>(audit.Path);

        var tool = Assert.Single(
            await harness.Client.ListToolsAsync(),
            candidate => candidate.Name == "network_write");
        var schema = tool.ProtocolTool.InputSchema.GetRawText();

        // The legacy flat configure fields are gone from what an agent is told it may send...
        foreach (var legacy in new[] { "\"subnetName\"", "\"ioSystemName\"" })
        {
            Assert.DoesNotContain(legacy, schema, StringComparison.Ordinal);
        }

        // ...and the surviving scalars are offered once each, inside changes only.
        foreach (var scalar in new[] { "\"ipAddress\"", "\"subnetMask\"", "\"pnDeviceName\"" })
        {
            Assert.Equal(1, schema.Split(scalar).Length - 1);
        }

        // Finally, the nested selectors are advertised closed, so an unknown member is not merely
        // rejected at runtime but never offered.
        foreach (var member in new[] { "\"target\"", "\"changes\"", "\"nodeId\"", "\"subnet\"", "\"ioSystem\"" })
        {
            Assert.Contains(member, schema, StringComparison.Ordinal);
        }

        Assert.Contains("\"additionalProperties\":false", schema, StringComparison.Ordinal);
    }

    /// <summary>
    /// Malformed nested input has to be refused where an agent actually sends it — through
    /// <c>tools/call</c> — not merely where a test can construct the CLR type. A member that is
    /// silently dropped here would turn "connect this subnet" into "change only the IP address"
    /// and still hand back a safety token, so the assertion is that nothing was previewed.
    /// </summary>
    [Theory]
    // An unknown nested member must be refused rather than silently dropped...
    [InlineData("unknown-nested-member", """{"deviceName":"PLC_1","nodeId":"n1","interfaceName":"PROFINET"}""", null)]
    // ...as must a legacy flat field an older caller might still send...
    [InlineData("legacy-flat-field", null, """{"ipAddress":"192.168.0.10","subnetName":"PN/IE_1"}""")]
    // ...and a nested value of the wrong JSON type.
    [InlineData("mistyped-target", "\"PLC_1\"", null)]
    [InlineData("mistyped-io-system-number", null, """{"ioSystem":{"subnetId":"S1","number":"first"}}""")]
    public async Task NetworkWrite_RejectsMalformedNestedInputAtTheProtocolBoundary(
        string _,
        string? targetJson,
        string? changesJson)
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartAsync<NetworkWriteTools>(audit.Path);

        var operation = new Dictionary<string, object?>
        {
            ["operationId"] = "configure",
            ["operation"] = "configure_network_device",
            ["projectPath"] = "network-roundtrip",
            ["target"] = JsonSerializer.Deserialize<JsonElement>(targetJson ?? """{"deviceName":"PLC_1","nodeId":"n1"}"""),
            ["changes"] = JsonSerializer.Deserialize<JsonElement>(changesJson ?? """{"ipAddress":"192.168.0.10"}"""),
        };

        var result = await CallWriteAsync(harness, new object[] { operation });

        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.DoesNotContain("safetyToken", text, StringComparison.Ordinal);
    }

    private static ValueTask<CallToolResult> CallWriteAsync(
        McpProtocolTestHarness harness,
        object operations,
        bool confirm = false,
        string? safetyToken = null)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["operations"] = operations,
            ["confirm"] = confirm,
        };

        if (safetyToken is not null)
        {
            arguments["safetyToken"] = safetyToken;
        }

        return harness.Client.CallToolAsync("network_write", arguments);
    }

    /// <summary>Runs one read_hardware_config through the actual MCP protocol and returns its
    /// decoded HardwareConfigInfo result element.</summary>
    private static async Task<JsonElement> ReadHardwareConfigAsync(McpProtocolTestHarness harness, string projectPath)
    {
        var result = await harness.Client.CallToolAsync(
            "network_read",
            new Dictionary<string, object?>
            {
                ["operations"] = new object[]
                {
                    new { operationId = "read", operation = "read_hardware_config", projectPath },
                },
            });

        Assert.False(result.IsError);
        return AssertOneCanonicalDocument(result)
            .GetProperty("batch")
            .GetProperty("operations")[0]
            .GetProperty("result");
    }

    /// <summary>Walks every device/item/interface to find the one node reporting <paramref name="nodeId"/>.</summary>
    private static JsonElement FindNodeByNodeId(JsonElement hardwareConfig, string nodeId)
        => hardwareConfig.GetProperty("devices")
            .EnumerateArray()
            .SelectMany(device => device.GetProperty("items").EnumerateArray())
            .SelectMany(item => item.GetProperty("networkInterfaces").EnumerateArray())
            .SelectMany(networkInterface => networkInterface.GetProperty("nodes").EnumerateArray())
            .Single(node => node.GetProperty("nodeId").GetString() == nodeId);

    private static object[] WriteOperations(string projectPath) => new object[]
    {
        new
        {
            operationId = "add",
            operation = "add_network_device",
            projectPath,
            typeIdentifier = "OrderNumber:TEST",
            deviceName = "PLC_1"
        },
        new
        {
            operationId = "configure",
            operation = "configure_network_device",
            projectPath,
            target = new { deviceName = "PLC_1", nodeId = "node-1" },
            changes = new { ipAddress = "192.168.0.10" }
        },
    };

    /// <summary>Asserts the discriminated envelope reports <paramref name="phase"/> and populates only
    /// <paramref name="populated"/> out of preview/batch/error.</summary>
    private static void AssertOnlyPopulated(JsonElement root, string phase, string populated)
    {
        Assert.Equal(phase, root.GetProperty("phase").GetString());
        foreach (var member in new[] { "preview", "batch", "error" })
        {
            Assert.Equal(
                member == populated ? JsonValueKind.Object : JsonValueKind.Null,
                root.GetProperty(member).ValueKind);
        }
    }

    /// <summary>Asserts the text block and structuredContent are the same canonical document.</summary>
    private static JsonElement AssertOneCanonicalDocument(CallToolResult result)
    {
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        using var textDocument = JsonDocument.Parse(text);
        Assert.True(JsonElement.DeepEquals(structured, textDocument.RootElement));
        return structured;
    }
}
