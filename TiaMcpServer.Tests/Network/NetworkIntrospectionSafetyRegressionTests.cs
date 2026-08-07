using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

[Collection("Mcp protocol serial")]
public class NetworkIntrospectionSafetyRegressionTests
{
    [Fact]
    public async Task Phase2WriteToken_AcceptsIdenticalHardwareState()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartAsync<NetworkWriteTools>(audit.Path);
        var operations = ConfigureOperation("network-roundtrip", "PLC_1", "node-1");

        var preview = AssertCanonical(await CallWriteAsync(harness, operations));
        var token = preview.GetProperty("preview").GetProperty("safetyToken").GetString();
        var appliedResult = await CallWriteAsync(harness, operations, confirm: true, safetyToken: token);
        var applied = AssertCanonical(appliedResult);

        Assert.False(appliedResult.IsError);
        Assert.Equal("apply", applied.GetProperty("phase").GetString());
        Assert.True(applied.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Phase2WriteToken_RejectsChangedHardwareState()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartAsync<NetworkWriteTools>(audit.Path);
        var operations = ConfigureOperation("network-state-seq", "PLC_2", "node-1");

        var preview = AssertCanonical(await CallWriteAsync(harness, operations));
        var token = preview.GetProperty("preview").GetProperty("safetyToken").GetString();
        var rejectedResult = await CallWriteAsync(harness, operations, confirm: true, safetyToken: token);
        var rejected = AssertCanonical(rejectedResult);

        Assert.True(rejectedResult.IsError);
        Assert.Equal("error", rejected.GetProperty("phase").GetString());
        Assert.Equal(
            WorkerFailureCategories.StateChanged,
            rejected.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, rejected.GetProperty("batch").ValueKind);
    }

    private static object[] ConfigureOperation(string projectPath, string deviceName, string nodeId)
        => new object[]
        {
            new
            {
                operationId = "configure",
                operation = "configure_network_device",
                projectPath,
                target = new { deviceName, nodeId },
                changes = new { ipAddress = "192.168.0.99" },
            },
        };

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

    private static JsonElement AssertCanonical(CallToolResult result)
    {
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal(CanonicalJson.Serialize(structured), text);
        return structured;
    }
}
