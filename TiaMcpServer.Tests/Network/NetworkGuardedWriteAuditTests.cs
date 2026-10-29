using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.TestSupport;
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
    [InlineData(McpAccessMode.ReadWrite, false, "null-items", "error", "none")]
    [InlineData(McpAccessMode.ReadWrite, false, "null-item", "error", "none")]
    [InlineData(McpAccessMode.ReadWrite, false, "binding", "error", "none")]
    [InlineData(McpAccessMode.ReadWrite, false, "network-guarded-rejected-setting", "applied", "none")]
    [InlineData(McpAccessMode.ReadWrite, false, "network-guarded-incomplete", "blocked", "none")]
    [InlineData(McpAccessMode.Full, false, "network-guarded-incomplete", "blocked", "none")]
    [InlineData(McpAccessMode.ReadWrite, false, "network-guarded-lost-node", "applied", "none")]
    [InlineData(McpAccessMode.ReadWrite, false, "network-guarded-unknown-result", "applied", "none")]
    public async Task WriteEntry_ProducesExpectedAudit(McpAccessMode mode, bool dryRun, string scenario, string phase, string by)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        var preflightFailure = !scenario.StartsWith("network-guarded", StringComparison.Ordinal);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, audit.Path,
            preflightFailure ? null : scenario);
        var args = NetworkGuardedWriteMcpTests.Arguments();
        args["dryRun"] = dryRun;
        if (scenario == "validation") args["operations"] = Array.Empty<object>();
        if (scenario == "null-items") args["operations"] = null;
        if (scenario == "null-item") args["operations"] = new object?[] { null };
        if (scenario == "network-guarded-rejected-setting")
        {
            var partial = NetworkGuardedWriteFixture.Configure("partial");
            partial.Changes = new() { IpAddress = "10.0.0.5", Subnet = new() { SubnetId = "590-3" } };
            args["operations"] = new[] { partial, NetworkGuardedWriteFixture.Delete() };
        }
        var reply = await harness.Client.CallToolAsync("network_write", args);
        var root = NetworkGuardedWriteMcpTests.Document(reply);
        Assert.Equal(phase, root.GetProperty("phase").GetString());
        Assert.Equal(phase is "blocked" or "error", reply.IsError == true);
        if (preflightFailure)
        {
            Assert.Empty(requests.Methods());
            Assert.Equal(scenario == "binding" ? "binding_conflict" : "validation_error",
                root.GetProperty("error").GetProperty("category").GetString());
        }
        var auditLines = NetworkGuardedWriteMcpTests.AuditLines(audit.Path);
        if (scenario == "null-items")
        {
            ArgumentRejectionAssert.IsCanonicalValidationError(reply, "network_write");
            Assert.Empty(auditLines);
            return;
        }
        using var record = JsonDocument.Parse(Assert.Single(auditLines));
        Assert.Equal(2, record.RootElement.GetProperty("recordVersion").GetInt32());
        Assert.Equal(by, record.RootElement.GetProperty("confirmation").GetProperty("by").GetString());
        Assert.All(record.RootElement.GetProperty("guards").EnumerateArray(), g => Assert.Equal(JsonValueKind.Null, g.GetProperty("satisfiedBy").ValueKind));
        Assert.Equal(Assert.Single(reply.Content.OfType<TextContentBlock>()).Text, record.RootElement.GetProperty("responseText").GetString());
        if (scenario is "network-guarded-unknown-result" or "network-guarded-rejected-setting")
        {
            Assert.False(root.GetProperty("success").GetBoolean());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
            Assert.Equal(JsonValueKind.Object, root.GetProperty("batch").ValueKind);
        }
        if (scenario == "network-guarded-rejected-setting")
        {
            var items = root.GetProperty("batch").GetProperty("operations");
            Assert.Equal("failed", items[0].GetProperty("status").GetString());
            Assert.Equal("10.0.0.5", items[0].GetProperty("result").GetProperty("appliedSettings").GetProperty("Address").GetString());
            Assert.Equal("engineering_rejected", items[0].GetProperty("failure").GetProperty("category").GetString());
            Assert.Equal("EngineeringTargetInvocationException", items[0].GetProperty("failure").GetProperty("engineeringError").GetProperty("type").GetString());
            Assert.Equal("earlierOperationFailed", items[1].GetProperty("skipReason").GetString());
            Assert.True(root.GetProperty("verification").GetProperty("success").GetBoolean());
            Assert.DoesNotContain("delete_subnet", requests.Methods());
        }
    }
}
