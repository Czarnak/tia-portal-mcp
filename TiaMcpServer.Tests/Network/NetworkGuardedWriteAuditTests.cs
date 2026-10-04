using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Network;

[Collection("Mcp protocol serial")]
public sealed class NetworkGuardedWriteAuditTests
{
    [Theory]
    [InlineData(McpAccessMode.ReadWrite, false, "network-guarded", "applied", "none")]
    [InlineData(McpAccessMode.Full, false, "network-guarded", "applied", "policy")]
    [InlineData(McpAccessMode.ReadWrite, true, "network-guarded", "preview", "none")]
    [InlineData(McpAccessMode.Full, true, "network-guarded", "preview", "none")]
    [InlineData(McpAccessMode.ReadWrite, false, "validation", "error", "none")]
    [InlineData(McpAccessMode.ReadWrite, false, "network-guarded-incomplete", "blocked", "none")]
    [InlineData(McpAccessMode.Full, false, "network-guarded-incomplete", "blocked", "none")]
    [InlineData(McpAccessMode.ReadWrite, false, "network-guarded-lost-node", "applied", "none")]
    [InlineData(McpAccessMode.ReadWrite, false, "network-guarded-unknown-result", "applied", "none")]
    public async Task WriteEntry_ProducesExactlyOneAudit(McpAccessMode mode, bool dryRun, string scenario, string phase, string by)
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, audit.Path,
            scenario == "validation" ? null : scenario);
        var args = NetworkGuardedWriteMcpTests.Arguments();
        args["dryRun"] = dryRun;
        if (scenario == "validation") args["operations"] = Array.Empty<object>();
        var reply = await harness.Client.CallToolAsync("network_write", args);
        var root = NetworkGuardedWriteMcpTests.Document(reply);
        Assert.Equal(phase, root.GetProperty("phase").GetString());
        Assert.Equal(phase is "blocked" or "error", reply.IsError == true);
        using var record = JsonDocument.Parse(Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
        Assert.Equal(2, record.RootElement.GetProperty("recordVersion").GetInt32());
        Assert.Equal(by, record.RootElement.GetProperty("confirmation").GetProperty("by").GetString());
        Assert.All(record.RootElement.GetProperty("guards").EnumerateArray(), g => Assert.Equal(JsonValueKind.Null, g.GetProperty("satisfiedBy").ValueKind));
        Assert.Equal(Assert.Single(reply.Content.OfType<TextContentBlock>()).Text, record.RootElement.GetProperty("responseText").GetString());
        if (scenario is "network-guarded-lost-node" or "network-guarded-unknown-result")
        {
            Assert.False(root.GetProperty("success").GetBoolean());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
            Assert.Equal(JsonValueKind.Object, root.GetProperty("batch").ValueKind);
        }
    }
}
