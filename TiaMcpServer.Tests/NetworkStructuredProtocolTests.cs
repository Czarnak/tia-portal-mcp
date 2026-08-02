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
            deviceName = "PLC_1",
            ipAddress = "192.168.0.10"
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
