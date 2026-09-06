using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Safety;

public sealed class TagOperationSafetyReadPolicyTests
{
    [Theory]
    [InlineData("read_create_tag_table_safety_snapshot")]
    [InlineData("read_delete_tag_table_safety_snapshot")]
    [InlineData("read_create_tag_safety_snapshot")]
    [InlineData("read_update_tag_safety_snapshot")]
    [InlineData("read_delete_tag_safety_snapshot")]
    [InlineData("read_create_user_constant_safety_snapshot")]
    [InlineData("read_update_user_constant_safety_snapshot")]
    [InlineData("read_delete_user_constant_safety_snapshot")]
    public void EveryTagSafetyReader_UsesTheSafetyReadIdentityBoundPolicy(string operation)
    {
        Assert.Equal(OperationCapability.SafetyRead, OperationPolicyCatalog.GetCapability(operation));
        Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(operation));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, operation));
    }
}
