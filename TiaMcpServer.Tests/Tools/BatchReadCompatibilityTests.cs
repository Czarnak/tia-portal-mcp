using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Hmi;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tools;
using TiaMcpServer.Vci;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

[Collection("Mcp protocol serial")]
public class BatchReadCompatibilityTests
{
    public static TheoryData<string> Tools => new() { "plc_read", "network_read", "hmi_read", "vci_read" };

    [Theory]
    [MemberData(nameof(Tools))]
    public async Task CanonicalResponsesMatchPreD1(string tool)
    {
        var (type, scenario, good, mixed) = tool switch
        {
            "plc_read" => (typeof(PlcReadTools), "plc-read-roundtrip",
                """{"operationId":"ok","operation":"get_block_content","blockPath":"PLC_1/Main"}""",
                """[{"operationId":"bad","operation":"get_block_content","blockPath":"PLC_1/Missing"},{"operationId":"ok","operation":"get_block_content","blockPath":"PLC_1/Main"}]"""),
            "network_read" => (typeof(NetworkReadTools), "network-mixed-results",
                """{"operationId":"ok","operation":"read_hardware_config","deviceName":"good"}""",
                """[{"operationId":"bad","operation":"read_hardware_config","deviceName":"worker-failure"},{"operationId":"protocol","operation":"read_hardware_config","deviceName":"contract-failure"},{"operationId":"ok","operation":"read_hardware_config","deviceName":"good"}]"""),
            "hmi_read" => (typeof(HmiReadTools), "hmi-read-roundtrip",
                """{"operationId":"ok","operation":"list_hmi_devices"}""",
                """[{"operationId":"bad","operation":"get_tag","hmiName":"HMI_1","tagName":"Missing"},{"operationId":"ok","operation":"list_hmi_devices"}]"""),
            "vci_read" => (typeof(VciReadTools), "vci-read-roundtrip",
                """{"operationId":"ok","operation":"list_workspaces"}""",
                """[{"operationId":"bad","operation":"list_mappings","workspaceName":"Missing"},{"operationId":"protocol","operation":"list_mappings","workspaceName":"Malformed"},{"operationId":"ok","operation":"list_workspaces"}]"""),
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };

        using var snapshot = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Tools", "Snapshots", "batch-read-responses.json")));
        var expected = snapshot.RootElement.GetProperty(tool);
        foreach (var bound in new[] { false, true })
        {
            await using var harness = await McpProtocolTestHarness.StartAsync(
                McpAccessMode.ReadOnly, builder => builder.WithProductionTools(type),
                startupProjectPath: bound ? scenario : null);
            var cases = bound
                ? new[] { ("success", $"[{good}]"), ("mixed", mixed) }
                : new[] { ("unbound", $"[{good}]"), ("empty", "[]"), ("invalid", "[{\"operationId\":\"bad\",\"operation\":\"unknown\"}]"), ("null-item", "[null]") };
            foreach (var (name, operations) in cases)
            {
                using var arguments = JsonDocument.Parse(operations);
                var result = await harness.Client.CallToolAsync(tool, new Dictionary<string, object?>
                {
                    ["operations"] = arguments.RootElement.Clone(),
                });
                var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
                Assert.Equal(text, CanonicalJson.Serialize(result.StructuredContent!.Value));
                Assert.Equal(tool == "hmi_read" ? "1.1" : "1.0", result.StructuredContent.Value.GetProperty("contractVersion").GetString());
                Assert.Equal(!bound, result.IsError == true);
                Assert.Equal(CanonicalJson.Serialize(expected.GetProperty(name)), text);
                Assert.Equal(name == "success", result.StructuredContent.Value.GetProperty("success").GetBoolean());
            }
        }
    }
}
