using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class ProjectTreeSafetyIdentityEnforcementTests
{
    [Theory]
    [InlineData("read_create_block_safety_snapshot")]
    [InlineData("read_create_block_group_safety_snapshot")]
    [InlineData("read_delete_block_group_safety_snapshot")]
    public async Task InternalTreeSafetyRead_RejectsMissingExpectedSessionIdentity(string method)
    {
        using var transport = new PersistentWorkerTransport(FakeWorkerLocator.Locate(), TimeSpan.FromSeconds(5));
        var observed = await transport.SendAsync(new WorkerRequest
        {
            Method = "read_hardware_config",
            ProjectPath = "tree-safety-request-echo"
        });
        Assert.True(observed.Success, observed.Error);
        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = method,
            ProjectPath = "tree-safety-request-echo"
        });
        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
    }
}
