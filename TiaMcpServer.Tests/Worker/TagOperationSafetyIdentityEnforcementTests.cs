using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class TagOperationSafetyIdentityEnforcementTests
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
    public async Task EveryTagSafetyReader_RejectsMissingExpectedSessionIdentity(string method)
    {
        using var transport = new PersistentWorkerTransport(FakeWorkerLocator.Locate(), TimeSpan.FromSeconds(5));
        var observed = await transport.SendAsync(new WorkerRequest { Method = "read_hardware_config", ProjectPath = "echo" });
        Assert.True(observed.Success, observed.Error);
        var response = await transport.SendAsync(new WorkerRequest { Method = method, ProjectPath = "echo" });
        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
    }
}
