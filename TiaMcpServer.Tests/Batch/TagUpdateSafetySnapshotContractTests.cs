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
    public void SnapshotRead_IsAReusableIdentityRequiredSafetyRead()
    {
        const string method = "read_update_tag_safety_snapshot";

        Assert.Equal(OperationCapability.SafetyRead, OperationPolicyCatalog.GetCapability(method));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
        Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(method));
    }
}
