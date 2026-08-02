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

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        using var textDocument = JsonDocument.Parse(text);
        Assert.True(JsonElement.DeepEquals(structured, textDocument.RootElement));
        Assert.Equal(
            JsonValueKind.Object,
            structured.GetProperty("batch")
                .GetProperty("operations")[0]
                .GetProperty("result").ValueKind);
    }
}
