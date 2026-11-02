using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.TestSupport;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class FakeWorkerClientsTests
{
    [Fact]
    public void Create_DefaultsToUnboundReadWrite()
    {
        using var client = FakeWorkerClients.Create();

        Assert.Equal(McpAccessMode.ReadWrite, client.AccessPolicy.Mode);
        Assert.Equal(ProjectBindingSnapshot.UnboundState, client.BindingSnapshot.State);
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly)]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public void Create_PreservesAccessMode(McpAccessMode mode)
    {
        using var client = FakeWorkerClients.Create(mode);

        Assert.Equal(mode, client.AccessPolicy.Mode);
    }

    [Fact]
    public async Task Create_PreservesCallerBindingAndExplicitWorkerPath()
    {
        var binding = new ProjectSessionBinding(null);
        var workerPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe");
        using var client = FakeWorkerClients.Create(binding: binding, workerPath: workerPath);

        var result = await client.GetProjectStatusAsync(null);

        binding.Invalidate("caller changed binding");

        Assert.True(binding.CaptureSnapshot().SameBinding(client.BindingSnapshot));
        Assert.Equal("caller changed binding", client.BindingSnapshot.InvalidatedReason);
        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, result.FailureCategory);
        Assert.Equal(WorkerDispatchState.NotSent, result.DispatchState);
        Assert.Contains(workerPath, result.Error);
    }

    [Fact]
    public async Task CreateBoundAsync_VerifiesTheRequestedScenario()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync("echo", McpAccessMode.ReadOnly);

        Assert.True(client.BindingSnapshot.IsVerified);
        Assert.Equal(ProjectPathNormalization.Canonicalize("echo"), client.BindingSnapshot.ProjectPath);
        Assert.Equal(McpAccessMode.ReadOnly, client.AccessPolicy.Mode);
    }
}
