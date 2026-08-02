using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Network;
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
