using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class TagUpdateSafetySnapshotContractTests
{
    [Fact]
    public void Snapshot_SerializesFalseDifferentlyFromUnavailable()
    {
        var concrete = JsonSerializer.Serialize(new TagUpdateSafetySnapshot(
            "ResolvedPLC", "/", "Default tag table", "MotorReady", "Bool", "%I0.0", false, true, false));
        var unavailable = JsonSerializer.Serialize(new TagUpdateSafetySnapshot(
            "ResolvedPLC", "/", "Default tag table", "MotorReady", "Bool", "%I0.0", null, null, null));

        Assert.Contains("\"externalAccessible\":false", concrete, StringComparison.Ordinal);
        Assert.Contains("\"externalAccessible\":null", unavailable, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_SerializesTheCompleteResolvedExactTarget()
    {
        var json = JsonSerializer.Serialize(new TagUpdateSafetySnapshot(
            "ResolvedPLC", "/Safety", "Default tag table", "MotorReady", "Bool", "%I0.0", false, null, true));

        using var document = JsonDocument.Parse(json);
        var target = document.RootElement;
        Assert.Equal("ResolvedPLC", target.GetProperty("plcName").GetString());
        Assert.Equal("/Safety", target.GetProperty("folderPath").GetString());
        Assert.Equal("Default tag table", target.GetProperty("tableName").GetString());
        Assert.Equal("MotorReady", target.GetProperty("tagName").GetString());
        Assert.Equal("Bool", target.GetProperty("dataType").GetString());
        Assert.Equal("%I0.0", target.GetProperty("logicalAddress").GetString());
        Assert.False(target.GetProperty("externalAccessible").GetBoolean());
        Assert.Equal(JsonValueKind.Null, target.GetProperty("externalVisible").ValueKind);
        Assert.True(target.GetProperty("externalWritable").GetBoolean());
    }

    [Fact]
    public void SnapshotRead_IsAReusableIdentityRequiredSafetyRead()
    {
        const string method = "read_update_tag_safety_snapshot";

        Assert.Equal(OperationCapability.SafetyRead, OperationPolicyCatalog.GetCapability(method));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
        Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(method));
    }
}
